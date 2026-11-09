using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Windows;

// Attributs d'assembly déclarés manuellement (GenerateAssemblyInfo=false dans le .csproj)
// pour éviter le bug CS0579 "attribut en double" du SDK WPF (projet wpftmp).
[assembly: AssemblyTitle("GestionCoutureApp")]
[assembly: AssemblyDescription("Application de gestion pour atelier de couture")]
[assembly: AssemblyCompany("GestionCoutureApp")]
[assembly: AssemblyProduct("GestionCoutureApp")]
[assembly: AssemblyVersion("2.1.0")]
[assembly: AssemblyFileVersion("2.1.0")]
[assembly: AssemblyInformationalVersion("2.1.0")]
[assembly: AssemblyConfiguration("Debug")]

// Accès internal depuis le projet de tests
[assembly: InternalsVisibleTo("GestionCoutureApp.Tests")]

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly
)]
