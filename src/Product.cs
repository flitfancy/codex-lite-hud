using System.Reflection;

[assembly:AssemblyTitle(Product.Name)]
[assembly:AssemblyProduct(Product.Name)]
[assembly:AssemblyCompany("FlitFancy")]
[assembly:AssemblyCopyright("Copyright (c) 2026 FlitFancy")]
[assembly:AssemblyVersion(BuildInfo.AssemblyVersion)]
[assembly:AssemblyFileVersion(BuildInfo.AssemblyVersion)]
[assembly:AssemblyInformationalVersion(BuildInfo.Version)]

static class Product
{
    internal const string Name="Codex Lite HUD";
    internal const string Version=BuildInfo.Version;
}
