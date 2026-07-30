import os

main_csproj = r'c:\Users\USER\GestionCoutureApp\GestionCoutureApp.csproj'

content = (
'<Project Sdk="Microsoft.NET.Sdk">\n'
'\n'
'  <PropertyGroup>\n'
'    <OutputType>WinExe</OutputType>\n'
'    <TargetFramework>net8.0-windows</TargetFramework>\n'
'    <Nullable>enable</Nullable>\n'
'    <ImplicitUsings>enable</ImplicitUsings>\n'
'    <UseWPF>true</UseWPF>\n'
'    <NoWarn>$(NoWarn);NU1701</NoWarn>\n'
'  </PropertyGroup>\n'
'\n'
'  <ItemGroup>\n'
'    <Resource Include="Resources\\logo_retouche_choco.png" />\n'
'  </ItemGroup>\n'
'\n'
'  <ItemGroup>\n'
'    <!-- DETTE TECHNIQUE : AForge.Video / AForge.Video.DirectShow (v2.2.5, 2013)\n'
'         Non maintenus depuis 2013 (API DirectShow COM).\n'
'         Migration recommandee : OpenCvSharp4.Windows ou Windows.Media.Capture.\n'
'         Impact : Views/WebcamCaptureWindow.cs uniquement. -->\n'
'    <PackageReference Include="AForge.Video" Version="2.2.5" NoWarn="NU1701" />\n'
'    <PackageReference Include="AForge.Video.DirectShow" Version="2.2.5" NoWarn="NU1701" />\n'
'    <!-- CommunityToolkit.Mvvm retire : non utilise (app 100% code-behind).\n'
'         A reintroduire lors d une migration MVVM progressive. -->\n'
'    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.11">\n'
'      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>\n'
'      <PrivateAssets>all</PrivateAssets>\n'
'    </PackageReference>\n'
'    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="8.0.11" />\n'
'    <!-- Reference explicite : utilisee directement par BackupService (VACUUM INTO). -->\n'
'    <PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.11" />\n'
'    <PackageReference Include="Microsoft.EntityFrameworkCore.Tools" Version="8.0.11">\n'
'      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>\n'
'      <PrivateAssets>all</PrivateAssets>\n'
'    </PackageReference>\n'
'    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.1" />\n'
'    <PackageReference Include="Microsoft.Extensions.Logging.Debug" Version="8.0.1" />\n'
'    <PackageReference Include="System.Drawing.Common" Version="8.0.19" />\n'
'  </ItemGroup>\n'
'\n'
'  <!-- Exclure le sous-dossier de tests du SDK WPF (evite que le compilateur\n'
'       wpftmp ramasse les fichiers xUnit et echoue). -->\n'
'  <ItemGroup>\n'
'    <Compile Remove="GestionCoutureApp.Tests\\**" />\n'
'    <EmbeddedResource Remove="GestionCoutureApp.Tests\\**" />\n'
'    <None Remove="GestionCoutureApp.Tests\\**" />\n'
'    <Content Remove="GestionCoutureApp.Tests\\**" />\n'
'  </ItemGroup>\n'
'\n'
'</Project>\n'
)

with open(main_csproj, 'w', encoding='utf-8') as f:
    f.write(content)

# Verifier
with open(main_csproj, 'rb') as f:
    first4 = f.read(4)
with open(main_csproj, 'r', encoding='utf-8') as f:
    last_line = f.readlines()[-1].strip()

print('First bytes (no BOM expected):', first4.hex())
print('Last line:', repr(last_line))
assert first4[:3] != b'\xef\xbb\xbf', 'BOM present!'
assert last_line == '</Project>', f'Bad closing tag: {last_line}'
print('OK — GestionCoutureApp.csproj repaired')
