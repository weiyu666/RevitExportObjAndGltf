Plug-in export obj and gltf format based on Revit:

主要使用了RevitAPI.dll 与RevitAPIUI.dll 来Revit的二次开发，其中 ：
//add-in manger 只读模式
[Transaction(TransactionMode.ReadOnly)]
调试的方式“附加到进程” -> revit 推荐使用vs2019，本人使用vs2017有时候进不到断点中去，这个是编译器出Bug了!

使用了SharpGLTF库，SharpGLTF是一个100％.NET标准库，旨在支持Khronos Group glTF 2.0文件格式。
所以使用SharpGLTF生成gltf、glb数据；
该库分为两个主要软件包：
SharpGLTF.Core提供读/写文件支持，以及对glTF模型的低级别访问。
SharpGLTF.Toolkit提供了方便的实用程序来帮助创建，操纵和评估glTF模型。


simple example gltf保存为glb格式：
var model = SharpGLTF.Schema2.ModelRoot.Load("model.gltf");
model.SaveGLB("model.glb");

思想：
五行代码搞定导出自定义格式，其中最关键的是IExportContext，需要继承并且实现该接口（主要工作都在这里）
IExportContext pExport = new CMyExporter();
CustomExporter exporter = new CustomExporter(doc, pExport);
 exporter.IncludeGeometricObjects = false;
exporter.ShouldStopOnError = true;
exporter.Export(view3D);

执行exporter.Export(view3D);后才进行执行IExportContext的Start；

IExportContext接口在数据导出中，执行如下的顺序:
    将revit的数据解析为我们自己的数据需要继承重写IExportContext就能revit文件进行数据导出和数据转换；
     * 接口在数据导出中，无链接模型执行如下的顺序:
     * Start  -> OnViewBegin   ->   onElementBegin -> OnInstanceBegin ->OnMaterial ->OnLight
     * ->OnFaceBegin OnPolymesh -> OnFaceEnd -> OnInstanceEnd-> OnElementEnd  
     *  ->OnViewEnd ->IsCanceled ->Finish、
     * 假如有链接模型在执行完非链接的OnElementBegin以后，执行OnLinkBegin，然后执行链接模型里的OnElementBegin……依次类推
  
依赖环境：Autodesk.RevitAPi Autodesk.Revit.UI  安装nodejs
使用工具：使用npm  安装gltf-pipeline配置系统环境

参考资料：
gltf格式       https://zhuanlan.zhihu.com/p/65265611
解决材质的问题  https://zhuanlan.zhihu.com/p/80465384   
