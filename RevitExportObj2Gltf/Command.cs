using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using App = Autodesk.Revit.ApplicationServices;
using System.IO;
using System.Diagnostics;
using Autodesk.Revit.DB.Visual;
using System;

namespace RevitExportObj2Gltf
{
    [Transaction(TransactionMode.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            //以导出当前视图            
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            App.Application app = uiapp.Application;
            Document doc = uidoc.Document;
            //没打开文档         
            if (null == doc)
            {
                message = "Please open the project.";
                return Result.Failed;
            }
            //没有打开文档           
            if (null == uidoc)
            {
                message = "Please run this command in an active project document.";
                return Result.Failed;
            }
            //3D视图下
            View3D view = doc.ActiveView as View3D;
            if (null == view)
            {
                message = "Please run this command in a 3D view.";
                return Result.Failed;
            }
            //保存导出的文件 
            SaveFileDialog sdial = new SaveFileDialog();
            sdial.Filter = "gltf|*.gltf|glb|*.glb";
            if (sdial.ShowDialog() == DialogResult.OK)
            {
                //默认值lod为等级8 （达到减面的效果 可以导出高模瑜低模）
                int lodGltfValue = 8;
                int lodObjValue = 8;

                //AssetSet objlibraryAsset = commandData.Application.Application.get_Assets(AssetType.Appearance); //2018
                //IList<Asset> objlibraryAsset = commandData.Application.Application.GetAssets(AssetType.Appearance);//2020
                RevitExportObj2Gltf contextObj = new RevitExportObj2Gltf(doc, sdial.FileName, lodObjValue);
                MyGltfExportContext contextGltf = new MyGltfExportContext(doc, lodGltfValue);

                try
                {

                    //拿到revit的doc  CustomExporter 用户自定义导出              
                    using (CustomExporter exporterObj = new CustomExporter(doc, contextObj))
                    {
                        //是否包括Geom对象
                        exporterObj.IncludeGeometricObjects = false;
                        exporterObj.ShouldStopOnError = true;
                        //导出3D模型                 
                        exporterObj.Export(view);
                    }
                    using (CustomExporter exporterGltf = new CustomExporter(doc, contextGltf))
                    {
                        //是否包括Geom对象                    
                        exporterGltf.IncludeGeometricObjects = false;
                        exporterGltf.ShouldStopOnError = true;
                        //导出3D模型                   
                        exporterGltf.Export(view);
                        contextGltf._model.SaveGLB(sdial.FileName);
                        contextGltf._model.SaveGLTF(sdial.FileName);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("提示信息:" + ex.Message);
                }

                System.Diagnostics.Process p = new System.Diagnostics.Process();
                p.StartInfo.FileName = "cmd.exe";
                p.StartInfo.UseShellExecute = false;
                //是否使用操作系统shell启动            
                p.StartInfo.RedirectStandardInput = true;
                //接受来自调用程序的输入信息                
                p.StartInfo.RedirectStandardOutput = true;
                //由调用程序获取输出信息             
                p.StartInfo.RedirectStandardError = true;
                //重定向标准错误输出               
                p.StartInfo.CreateNoWindow = true;//不显示程序窗口  
                p.Start();
                //启动程序                         
                //使用gltf pipeline命令行工具                        
                //向cmd窗口发送输入信息  （node.js已经是配置好了系统环境变量）                  
                //string str = @"cd D:\cmder";                         
                //p.StandardInput.WriteLine(str);              
                ////obj2gltf -i model.obj -o model.gltf                 
                ////obj转gltf                
                //string obj2GltfStr = string.Format("obj2gltf -i {0} -o {1}", Path.GetDirectoryName(sdial.FileName) + "\\" +             
                //    Path.GetFileNameWithoutExtension(sdial.FileName) + ".obj", Path.GetDirectoryName(sdial.FileName) + "\\" + Path.GetFileNameWithoutExtension(sdial.FileName) + ".gltf");          
                //p.StandardInput.WriteLine(obj2GltfStr);               
                //Debug.Print("obj2gltf successful.");              
                //运用Draco算法将GLB压缩               
                string glbName = Path.GetFileNameWithoutExtension(sdial.FileName) + "(Draco)" + ".glb";
                string glbstr = string.Format("gltf-pipeline.cmd gltf-pipeline -i {0} -o {1}", sdial.FileName, Path.GetDirectoryName(sdial.FileName) + "\\" + glbName);
                p.StandardInput.WriteLine(glbstr);
                //gltf-pipeline.c md gltf-pipeline -i model.gltf -o modelDraco.gltf -d            
                //运用Draco算法将GLTF压缩              
                string gltfDracoName = Path.GetFileNameWithoutExtension(sdial.FileName) + "(Draco)" + ".gltf";
                string gltfDraco = string.Format("gltf-pipeline.cmd gltf-pipeline -i {0} -o {1} -d", sdial.FileName, Path.GetDirectoryName(sdial.FileName) + "\\" + gltfDracoName);
                p.StandardInput.WriteLine(gltfDraco);
                p.StandardInput.AutoFlush = true;
                p.StandardInput.WriteLine("exit");
                //获取cmd窗口的输出信息               
                string output = p.StandardOutput.ReadToEnd();
                MessageBox.Show(output);
            }
            return Result.Succeeded;
        }
    }
}
