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
using System.IO;

namespace RevitExportObj2Gltf
{
    /*
     *将revit的数据解析为我们自己的数据需要继承重写IExportContext就能revit文件进行数据导出和数据转换；
     * 接口在数据导出中，无链接模型执行如下的顺序:
     * Start  -> OnViewBegin   ->   onElementBegin -> OnInstanceBegin ->OnMaterial ->OnLight
     * ->OnFaceBegin OnPolymesh -> OnFaceEnd -> OnInstanceEnd-> OnElementEnd  
     *  ->OnViewEnd ->IsCanceled ->Finish、
     * 假如有链接模型在执行完非链接的OnElementBegin以后，执行OnLinkBegin，然后执行链接模型里的OnElementBegin……依次类推
     */
    class RevitExportObj2Gltf : IExportContext
    {
        private StreamWriter swObj = null;
        private StreamWriter wObj = null;
        StreamWriter swMtl = null;
        const string strNewmtl = "\nnewmtl {0}\n" +
        "ka {1} {2} {3}\n" +
        "kd {1} {2} {3}\n" +
        "d {4}\n";

        // 分别用来传递MaterialId、颜色、透明度和材质集合。
        ElementId currentMaterialId = null;
        Color currentColor;
        double currentTransparency;
        Autodesk.Revit.DB.Visual.Asset currentAsset = null;
        private string _textureFolder;       //材质库地址
        
        private int _precision;//转换精度

        Document m_doc;
        Stack<Transform> m_TransformationStack = new Stack<Transform>();
        private ElementId currentId;
        /*用来记录索引起始值的偏移。因为用facet.V1、V2、V3取得的索引值是从0开始对应顶点列表的，
        *而obj文件中的索引值是从1开始对应整个文件中的顶点位置的，所以执行到下一个mesh时就要加上上一个mesh的顶点数量。
        */
        private int index;
        private string textureFolder = "";
        private string textureName = "";
        private string filePath;

        //构造函数
        public RevitExportObj2Gltf(Document doc, string path，int value)
        {
            filePath = path;
            m_doc = doc;
            m_TransformationStack.Push(Transform.Identity);//Transform.Identity 单位矩阵
             this._precision = value;
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

            swObj = new StreamWriter(Path.GetDirectoryName(filePath) + "\\" + Path.GetFileNameWithoutExtension(filePath) + ".obj");
            swObj.Write("mtllib test.mtl" + "\n");
            swMtl = new StreamWriter(Path.GetDirectoryName(filePath) + "\\" + Path.GetFileNameWithoutExtension(filePath) + ".mtl");

            //通过读取注册表相应键值获取材质库地址
            RegistryKey hklm = Registry.LocalMachine;
            RegistryKey libraryPath = hklm.OpenSubKey("SOFTWARE\\Wow6432Node\\Autodesk\\ADSKTextureLibrary\\1");
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
            //导出3D视图 对视图没什么要处理的 直接： return RenderNodeAction.Proceed;
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
            //入栈
            Debug.Print($"LinkBegin {node.NodeName}");
            m_TransformationStack.Push(m_TransformationStack.Peek().Multiply(node.GetTransform()));
            return RenderNodeAction.Proceed;
        }

        /// <summary>
        /// 此方法标记要导出的图元的开始
        /// </summary>
        /// <param name="elementId"></param>
        /// <returns></returns>
        public RenderNodeAction OnElementBegin(ElementId elementId)
        {
            Debug.Print($"ElementBegin {elementId.IntegerValue}");
            //elementId作为obj文件里的对象名
            currentId = elementId;
            return RenderNodeAction.Proceed;
        }

        /// <summary>
        /// 此方法标记了要导出的实例的开始。（入栈）
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public RenderNodeAction OnInstanceBegin(InstanceNode node)
        {
            Debug.Print($"InstanceBegin {node.NodeName}");
            m_TransformationStack.Push(m_TransformationStack.Peek().Multiply(node.GetTransform()));
            return RenderNodeAction.Proceed;
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
            if (currentMaterialId != node.MaterialId)
            {
                currentMaterialId = node.MaterialId;
                currentColor = node.Color;
                currentTransparency = node.Transparency;
                swMtl.Write(strNewmtl, currentMaterialId.IntegerValue.ToString(),
                (currentColor.Red / 256.0).ToString(), (currentColor.Green / 256.0).ToString(), (currentColor.Blue / 256.0).ToString(),
                currentTransparency);
                if (node.HasOverriddenAppearance)
                {
                    currentAsset = node.GetAppearanceOverride();
                }
                else
                {
                    currentAsset = node.GetAppearance();
                }

                try
                {
                    //取得Asset中贴图信息
                    string textureFile = (FindTextureAsset(currentAsset as AssetProperty)["unifiedbitmap_Bitmap"] as AssetPropertyString).Value.Split('|')[0];
                    //用Asset中贴图信息和注册表里的材质库地址得到贴图文件所在位置
                    string texturePath = Path.Combine(textureFolder, textureFile.Replace("/", "\\"));
                    //写入贴图名称
                    swMtl.Write("map_Kd " + Path.GetFileName(texturePath) + "\n");
                    //如果贴图文件真实存在，就复制到相应位置
                    if (File.Exists(texturePath))
                    {
                        File.Copy(texturePath, Path.Combine(Path.GetDirectoryName(filePath), textureName), true);
                    }
                }
                catch (Exception e)
                {
                    Debug.Print("{0} Second exception.", e.Message);
                }
            }
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
            return RenderNodeAction.Proceed;
        }

        /// <summary>
        /// 输出Polymesh多边形网格，同时它也是属于face的
        /// </summary>
        /// <param name="node"></param>
        public void OnPolymesh(PolymeshTopology node)
        {
            //把当前ElementId作为对象名写入文件
            swObj.Write("o " + currentId.IntegerValue.ToString() + "\n");
            swObj.Write("usemtl " + currentMaterialId.IntegerValue.ToString() + "\n");

            //取得顶点坐标并进行位置转换
            Transform currentTransform = m_TransformationStack.Peek();
            IList<XYZ> points = node.GetPoints();
            points = points.Select(p => currentTransform.OfPoint(p)).ToList();

            //把顶点数据写入文件
            foreach (XYZ point in points)
            {
                swObj.Write("v " + point.X.ToString() + " " + point.Y.ToString() + " " + point.Z.ToString() + "\n");
            }

            //取得UV坐标
            IList<UV> uvs = node.GetUVs();

            //把UV数据写入文件        
            foreach (UV uv in uvs)
            {
                swObj.Write("vt " + uv.U.ToString() + " " + uv.V.ToString() + " 0.0000\n");
            }
            //取得面
            IList<PolymeshFacet> facets = node.GetFacets();

            //把面数据写入文件
            foreach (PolymeshFacet facet in facets)
            {
                swObj.Write("f " + (facet.V1 + 1 + index).ToString() + "/" + (facet.V1 + 1 + index).ToString() + " " + (facet.V2 + 1 + index).ToString() + "/" + (facet.V2 + 1 + index).ToString() + " " + (facet.V3 + 1 + index).ToString() + "/" + (facet.V3 + 1 + index).ToString() + "\n");
            }
            index += node.NumberOfPoints;
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
        /// 此方法标志着要导出的实例的结束。（出栈）
        /// </summary>
        /// <param name="node"></param>
        public void OnInstanceEnd(InstanceNode node)
        {
            Debug.Print($"InstanceEnd {node.NodeName}");
            m_TransformationStack.Pop();
        }

        /// <summary>
        /// 导出图元结束
        /// </summary>
        /// <param name="elementId"></param>
        public void OnElementEnd(ElementId elementId)
        {

        }

        /// <summary>
        /// 如果是链接模型，这里就是链接模型结束
        /// </summary>
        /// <param name="node"></param>
        public void OnLinkEnd(LinkNode node)
        {
            //对应出栈
            Debug.Print($"LinkEnd {node.NodeName}");
            m_TransformationStack.Pop();
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
            swObj.Close();
            swMtl.Close();
            Debug.Print("Finish");
        }

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
        /// 自定义方法，用递归找到包含贴图信息：Asset包含的AssetProperty有多种类型，其中Asset、Properties
        /// 和Reference这三种必须递归处理。贴图信息的AssetProperty名字是unifiedbitmap_Bitmap，类型是String。
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
