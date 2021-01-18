using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Numerics;
using Autodesk.Revit.DB;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Schema2;
using Autodesk.Revit.DB.Visual;
using Microsoft.Win32;

namespace RevitExportObj2Gltf
{
    //顶点坐标
    using VERTEX = VertexPosition;
    //材质
    using RMaterial = Autodesk.Revit.DB.Material;

    /*
     *将revit的数据解析为我们自己的数据需要继承重写IExportContext就能revit文件进行数据导出和数据转换；
     * 接口在数据导出中，无链接模型执行如下的顺序:
     * Start  -> OnViewBegin   ->   onElementBegin -> OnInstanceBegin ->OnMaterial ->OnLight
     * ->OnFaceBegin OnPolymesh -> OnFaceEnd -> OnInstanceEnd-> OnElementEnd  
     *  ->OnViewEnd ->IsCanceled ->Finish、
     * 假如有链接模型在执行完非链接的OnElementBegin以后，执行OnLinkBegin，然后执行链接模型里的OnElementBegin……依次类推
     */
    class MyGltfExportContext : IExportContext
    {
        //英寸转毫米
        const double _inch_to_mm = 25.4f;
        //英尺转毫米
        const double _foot_to_mm = 12 * _inch_to_mm;
        //英尺到米
        const double _foot_to_m = _foot_to_mm / 1000;
        //Root节点
        public ModelRoot _model;
        Scene _scene;
        Dictionary<string, MaterialBuilder> _materials = new Dictionary<string, MaterialBuilder>();
        MaterialBuilder _material;
        //opengl网格mesh
        MeshBuilder<VERTEX> _mesh;

        private int _precision;//转换精度
        //Document _doc;
        //Document _сdoc;

        Stack<Document> _documentStack = new Stack<Document>();
        Stack<Transform> _transformationStack = new Stack<Transform>();

        //构造函数
        public MyGltfExportContext(Document doc ,int precisionValue)
        {
            _documentStack.Push(doc);
            _transformationStack.Push(Transform.Identity);//Transform.Identity 单位矩阵
            this._precision = precisionValue;
        }

        Document CurrentDocument
        {
            get
            {
                return _documentStack.Peek();
            }
        }

        Transform CurrentTransform
        {
            get
            {
                return _transformationStack.Peek();
            }
        }

        /// <summary>
        /// This method is the starting point of the export process.
        /// </summary>
        /// <remarks>
        /// The method is called only once and is typically used to prepare
        /// the context object, e.g. crate or open the output files,
        /// or establish a connection to an on-line renderer, etc.
        /// </remarks>
        /// <returns>
        /// Return true if the export process it good to start.
        /// </returns>
        public bool Start()
        {
            Debug.Print("Start");

            _material = new MaterialBuilder()
                .WithDoubleSide(true)
                .WithMetallicRoughnessShader()
                .WithChannelParam("BaseColor", new Vector4(0.5f, 0.5f, 0.5f, 1));
            _material.UseChannel("MetallicRoughness");

            _materials.Add("Default", _material);

            _model = ModelRoot.CreateModel();
            _scene = _model.UseScene("Default");

            //通过读取注册表相应键值获取材质库地址
            RegistryKey hklm = Registry.LocalMachine;
            RegistryKey libraryPath = hklm.OpenSubKey("SOFTWARE\\Wow6432Node\\Autodesk\\ADSKAdvancedTextureLibrary\\1");
            _textureFolder = libraryPath.GetValue("LibraryPaths").ToString();
            hklm.Close();
            libraryPath.Close();

            //启动return ture
            return true;
        }


        /// <summary>
        /// This method marks the start of processing a view (a 3D view)
        /// </summary>
        public RenderNodeAction OnViewBegin(ViewNode node)
        {
            //导出3D视图
             /*0到15 默认8 级别越小减面的程度越高，最优是0最低是15总共份16级
           * SolidOrShellTessellationControls.LevelOfDetail曲面细分着色器控制lod范围0到1；
           * ViewNode.LevelOfDetail是视图将呈现的详细程度，取值范围[0,15]Revit将在细分面时使用建议的详细程度； 否则，它将使用基于输出分辨率的默认算法。\
           * 如果要求明确的细节级别（即正值），则使用接近有效范围中间值的值会产生非常合理的细分。 Revit使用级别8作为其“正常” LoD。
           * 对于face.Triangulate(precision) 详细程度。 其范围是从0到1。0是最低的详细级别，而1是最高的详细级别。
           */
            node.LevelOfDetail = _precision;
            Debug.Print($"ViewBegin {node.NodeName}");
            return RenderNodeAction.Proceed;

        }

        /// <summary>
        /// 如果是链接模型，这里就是链接模型开始
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public RenderNodeAction OnLinkBegin(LinkNode node)
        {
            Debug.Print($"LinkBegin {node.NodeName}");
            //_сdoc = node.GetDocument();
            _documentStack.Push(node.GetDocument());
            _transformationStack.Push(CurrentTransform.Multiply(node.GetTransform()));
            return RenderNodeAction.Proceed;
        }

        /// <summary>
        /// 此方法标记要导出的图元的开始
        /// </summary>
        /// <param name="elementId"></param>
        /// <returns></returns>
        public RenderNodeAction OnElementBegin(ElementId elementId)
        {
            Element e = CurrentDocument.GetElement(elementId);
            if (e != null)
            {
                if (null == e.Category)
                {
                    Debug.WriteLine("\r\n*** Non-category element!\r\n");
                    return RenderNodeAction.Skip;
                }
            }

            //创建一个网格
            _mesh = new MeshBuilder<VERTEX>(elementId.IntegerValue.ToString());
            Debug.Print($"ElementBegin {elementId.IntegerValue}");
            return RenderNodeAction.Proceed;
        }

        /// <summary>
        /// 此方法标记了要导出的实例的开始。
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public RenderNodeAction OnInstanceBegin(InstanceNode node)
        {
            Debug.Print($"InstanceBegin {node.NodeName}");
            _transformationStack.Push(CurrentTransform.Multiply(node.GetTransform()));
            return RenderNodeAction.Proceed;
        }

        /// <summary>
        /// 设置当前材质
        /// </summary>
        /// <param name="uidMaterial"></param>
        void SetCurrentMaterial(string uidMaterial)
        {
            if (!_materials.ContainsKey(uidMaterial))
            {
                RMaterial material = CurrentDocument.GetElement(uidMaterial) as RMaterial;
                Color c = material.Color;
                MaterialBuilder m=null;
                try
                {
                   if (material.Transparency != 0)
                     {
                         m = new MaterialBuilder()
                        .WithAlpha(SharpGLTF.Materials.AlphaMode.BLEND)
                        .WithDoubleSide(true)
                        .WithMetallicRoughnessShader()
                        .WithChannelParam("BaseColor", new Vector4(c.Red / 256f, c.Green / 256f, c.Blue / 256f, 1 - (material.Transparency / 128f)));
                       // .WithChannelParam("BaseColor", new Vector4(currentColor.Red / 256f, currentColor.Green / 256f, currentColor.Blue / 256f, (float)currentTransparency));
                      }
                    else
                     {                 
                            m = new MaterialBuilder()
                                               .WithDoubleSide(true)
                                               .WithMetallicRoughnessShader()
                                               .WithChannelParam("BaseColor", new Vector4(c.Red / 256f, c.Green / 256f, c.Blue / 256f, 1));
                                              // .WithChannelParam("BaseColor", new Vector4(currentColor.Red / 256f, currentColor.Green / 256f, currentColor.Blue / 256f, (float)currentTransparency));
                   
                         }
                    }
                    catch(Exception e)
                    {
                     Debug.Print("{0} Second exception.", e.Message);
                    }                   
                m.UseChannel("MetallicRoughness");
                _materials.Add(uidMaterial, m);
            }
            _material = _materials[uidMaterial];
        }

        void SetDefaultMaterial()
        {
            _material = _materials["Default"];
        }

        /// <summary>
        /// 设置材质
        /// </summary>
        /// <remarks>
        //可以为每个单独的输出网格调用OnMaterial方法
        ///即使材质尚未实际更改。 因此通常
        ///有利于存储当前材料并仅获取其属性
        ///当材质实际更改时。
        /// </remarks>
        public void OnMaterial(MaterialNode node)
        {
            ElementId id = node.MaterialId;
            //是无效的objectID设置为默认的默认objectID
            if (ElementId.InvalidElementId != id)
            {
                Element m = CurrentDocument.GetElement
                    (node.MaterialId);
                SetCurrentMaterial(m.UniqueId);
            }
            else SetDefaultMaterial();

            Debug.Print($"Material {node.NodeName}");
        }

        public void OnLight(LightNode node)
        {
            //OnLight(LightNode node)方法，这个似乎是渲染时才有用，这里用不到，也留空。
            Debug.Print("OnLight not implemented.");
        }

        /// <summary>
        /// 此方法标志着RPC对象导出的开始。
        /// </summary>
        /// <param name="node"></param>
        public void OnRPC(RPCNode node)
        {
            //再看OnRPC(RPCNode node)方法，API里写清楚了，这个方法只在使用IPhotoRenderContext时发挥作用
            Debug.Print($"RPC {node.NodeName}");
        }

        /// <summary>
        /// 导出face面
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public RenderNodeAction OnFaceBegin(FaceNode node)
        {
            Debug.Print("OnFaceBegin not implemented.");
            return RenderNodeAction.Skip;
        }

        /// <summary>
        /// 输出Polymesh多边形网格，同时它也是属于face的
        /// </summary>
        /// <param name="node"></param>
        public void OnPolymesh(PolymeshTopology node)
        {
            //这里非常重要空间几何信息全都在这里
            int nPts = node.NumberOfPoints;
            int nFacets = node.NumberOfFacets;

            Debug.Print($"Polymesh : {nPts} vertices {nFacets} facets");

            IList<XYZ> vertices = node.GetPoints();
            IList<XYZ> normals = node.GetNormals();

            DistributionOfNormals distrib = node.DistributionOfNormals;

            VERTEX[] vertexs = new VERTEX[nPts];
            XYZ p;
            Transform t = CurrentTransform;
            for (int i = 0; i < nPts; i++)
            {
                p = t.OfPoint(node.GetPoint(i));
                //vertexs[i] = new VERTEX((float)(p.Y*_foot_to_m), (float)(p.Z*_foot_to_m), (float)(p.X*_foot_to_m));
                vertexs[i] = new VERTEX((float)(p.Y), (float)(p.Z), (float)(p.X));
            }

            var prim = _mesh.UsePrimitive(_material);

            PolymeshFacet f;
            for (int i = 0; i < nFacets; i++)
            {
                f = node.GetFacet(i);
                prim.AddTriangle(vertexs[f.V1], vertexs[f.V2], vertexs[f.V3]);
            }
        }

        /// <summary>
        /// 结束face的导出
        /// </summary>
        /// <param name="node"></param>
        public void OnFaceEnd(FaceNode node)
        {
            Debug.Print("OnFaceEnd not implemented.");
        }

        /// <summary>
        /// 此方法标志着要导出的实例的结束。
        /// </summary>
        /// <param name="node"></param>
        public void OnInstanceEnd(InstanceNode node)
        {
            Debug.Print($"InstanceEnd {node.NodeName}");
            _transformationStack.Pop();
        }
       
        /// <summary>
        /// 导出图元结束
        /// </summary>
        /// <param name="elementId"></param>
        public void OnElementEnd(ElementId elementId)
        {
            Element e = CurrentDocument.GetElement(elementId);
            if (e != null)
            {
                if (null == e.Category)
                {
                    Debug.WriteLine("\r\n*** Non-category element!\r\n");
                    return;
                }
            }

            if (_mesh.Primitives.Count > 0) _scene.CreateNode().WithMesh(_model.CreateMeshes(_mesh)[0]);
            Debug.Print($"ElementEnd {elementId.IntegerValue}");
        }

        /// <summary>
        /// 如果是链接模型，这里就是链接模型结束
        /// </summary>
        /// <param name="node"></param>
        public void OnLinkEnd(LinkNode node)
        {
            Debug.Print($"LinkEnd {node.NodeName}");
            _transformationStack.Pop();
            _documentStack.Pop();
        }

        /// <summary>
        /// This method marks the end of a 3D view being exported.
        /// </summary>
        /// <param name="elementId"></param>
        public void OnViewEnd(ElementId elementId)
        {
            Debug.Print($"ViewEnd {elementId.IntegerValue}");
        }

        public bool IsCanceled()
        {
            return false;
        }

        /// <summary>
        /// 在程序处理完所有之后（或取消处理之后），在导出过程的最后将调用此方法。
        /// </summary>
        public void Finish()
        {
            Debug.Print("Finish");
        }

        // 分别用来传递MaterialId、颜色、透明度和材质集合。
        ElementId currentMaterialId = null;
        Color currentColor;
        double currentTransparency;
        Autodesk.Revit.DB.Visual.Asset currentAsset = null;
        private string _textureFolder;       //材质库地址

        //public void OnMaterial(MaterialNode node)
        //{
        //    //是无效的objectID设置为默认的默认objectID
        //    if (ElementId.InvalidElementId != node.MaterialId)
        //    {
        //        Element m = CurrentDocument.GetElement(node.MaterialId);
        //        currentMaterialId = node.MaterialId;
        //        currentColor = node.Color;
        //        currentTransparency = node.Transparency;
        //       // currentMaterialId.IntegerValue
        //        SetCurrentMaterial(m.UniqueId);
        //    }
        //    else SetDefaultMaterial();

        //    Debug.Print($"Material {node.NodeName}");     
        //}

        /// <summary>
        /// 自定义方法，判断Asset是否包含贴图信息
        /// </summary>
        /// <param name="asset"></param>
        /// <returns></returns>
        private bool IsTextureAsset(Autodesk.Revit.DB.Visual.Asset asset)
        {
            AssetProperty assetProprty = GetAssetProprty(asset, "assettype");
            if (assetProprty != null && (assetProprty as AssetPropertyString).Value == "texture")
            {
                return true;
            }
            return GetAssetProprty(asset, "unifiedbitmap_Bitmap") != null;
        }

        /// <summary>
        /// 自定义方法，根据名字获取对应的AssetProprty
        /// </summary>
        /// <param name="asset"></param>
        /// <param name="propertyName"></param>
        /// <returns></returns>
        private AssetProperty GetAssetProprty(Autodesk.Revit.DB.Visual.Asset asset, string propertyName)
        {
            for (int i = 0; i < asset.Size; i++)
            {
                if (asset[i].Name == propertyName)
                {
                    return asset[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 自定义方法，用递归找到包含贴图信息：
        /// </summary>
        /// <param name="ap"></param>
        /// <returns></returns>
        private Autodesk.Revit.DB.Visual.Asset FindTextureAsset(AssetProperty ap)
        {
            Autodesk.Revit.DB.Visual.Asset result = null;
            if (ap.Type == AssetPropertyType.Asset)
            {
                if (!IsTextureAsset(ap as Autodesk.Revit.DB.Visual.Asset))
                {
                    for (int i = 0; i < (ap as Autodesk.Revit.DB.Visual.Asset).Size; i++)
                    {
                        if (null != FindTextureAsset((ap as Autodesk.Revit.DB.Visual.Asset)[i]))
                        {
                            result = FindTextureAsset((ap as Autodesk.Revit.DB.Visual.Asset)[i]);
                            break;
                        }
                    }
                }
                else
                {
                    result = ap as Autodesk.Revit.DB.Visual.Asset;
                }
                return result;
            }
            else
            {
                for (int j = 0; j < ap.NumberOfConnectedProperties; j++)
                {
                    if (null != FindTextureAsset(ap.GetConnectedProperty(j)))
                    {
                        result = FindTextureAsset(ap.GetConnectedProperty(j));
                    }
                }
                return result;
            }
        }
    }
}
