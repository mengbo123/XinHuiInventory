# XinHuiInventory
This is a simple, customizable inventory management system.


下载后直接发布成exe即可

dotnet build
如果是 Release 模式
dotnet build -c Release

重新生成
dotnet clean
dotnet build

发布
dotnet publish -c Release

单文件打包发布
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

