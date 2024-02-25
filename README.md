<p align="center">
  <img src="./images/OpenKH.png">
</p>

Aims to centralize all the technical knowledge of the 'Kingdom Hearts' game series in one place, providing documentation, tools, code libraries, and the foundation for modding the commercial games.

[![Build Status](https://github.com/OpenKh/OpenKh/actions/workflows/dotnet.yml/badge.svg)](https://github.com/OpenKH/OpenKh/actions/workflows/dotnet.yml)

## Documentation

All the documentation is located in the `/docs` folder in its raw form. A more web-friendly version can be accessed at: [https://openkh.dev/](https://openkh.dev/)

## Downloads

New builds of OpenKH are automatically generated every time one of the contributors inspects and approves a new proposed feature or fix. Those builds are considered stable as they are built from the `master` branch. The version format used in the builds is `YEAR.MONTH.DAY.BUILDID`.

[![OpenKh](https://img.shields.io/badge/OpenKh-Download-blue.svg)](https://github.com/OpenKH/OpenKh/releases)

All the builds from `master` and from pull requestes are generated from [GitHub Actions](https://github.com/OpenKh/OpenKh/actions).

OpenKH tools require the instllation of the [.NET 6.0 Runtime](https://dotnet.microsoft.com/download/dotnet/6.0). All the UI tools are designed to work on Windows, while command line tools will work on any operating system.


<p align="center">
  <img src="./images/Runtime.jpg" width="540">
</p>

Note: All CLI and GUI programs **should** be cross-platform, though extensive testing primarily happens on Windows systems. As such, users may be required to run GUI programs under a WINE prefix for Linux, Mac, BSD, etc.

## OpenKH in depth

<p align="center">
  <img src="./images/diagram.png" width="720">
</p>

From an architectural point of view, the code is structured to abstract low-level implementation such as file parsers and infrastructural logic to high-level functionalties such as 3D rendering or tools. The projects are layered to be able to share as much as code possible, but isolated in order to avoid coupling.

From a community perspective, OpenKH will provide the best form of documentation, modding portal and fan-game support that is derived from it.

## Build from source code

The minimum requirement is [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0). Once the repository is downloaded, `build.ps1` or `build.sh` needs be executed. This is determined by the operating system in use. Alternatively, for those who prefer an IDE environment under Windows, you can always open the included solution file `OpenKh.sln` in Visual Studio and compile through the `Build` menu.

## Additional info

### Future plans

* Provide a fully fledged and user friendly modding toolchain.
* Centralize modding downloads with a review system.
* Provide a friendly environment for mod users and creators alike.
* Create a community site and forum where users can openly interact with and help one another with modifications using OpenKH tools and documentation.
* Create a custom game engine that is compatible with assets from the retail games.

### Contribution

There is a [guide](CONTRIBUTING.md) describing how to contact the team and contribute to the project.

### License

The entire content of the repository is protected by the Apache 2.0 license. Some of the key points of the license are:

* You **can** copy, modify, and distribute the software.
* You **can** use this software privately.
* You **can** use this software for commercial purposes.
* You **can** append to the "NOTICE" file, if said file exists in the main repository.
* You **cannot** hold any contributor to the repository liable for damages.
* You **cannot** change or otherwise modify any patent, trademark, and attribution notices from the source repository.
* You **must** indicate changes made to the code, if any.
* You **must** include the same NOTICE file in every distribution, if included within the original repository.
* You **must** include the license and copyright notice with each and every distribution and fork.
* Any modifications of this code base **ABSOLUTELY MUST** be distributed with the same license, Apache 2.0.

### Modifications
OpenKh.Tools.MapStudio/App.cs has been modified to be able to extract map models, textures, rendering information and collision data from KH2 map files
If you run MapStudio then you can use `File>Export>World Meshes (Sliced Textures)` and `File>Export>Map Collision (Combined)` to export the map models and collision data respectively
it is not recommended to use the non-slided textures option because it will export the raw textures instead of ones already cropped to the correct size for rendering, and will use another output format which I have not documented
when exporting the map models it will also export the textures and a text file with details about how to render the map models
this text file uses this format:
\[mesh group index\],\[mesh index\]:\[texture name\]:\[alpha flags\]:\[priority\]:\[draw priority\]:\[U wrap mode\],\[V wrap mode\]
mesh group index and mesh index are used to find the mesh in the map model file
texture name is the name of the texture file (without the extension) for the mesh
alpha flags is an integer bitfield that is used to determine how the texture is rendered
priority and draw priority are somehow related to render order but I'm not sure exactly how because I haven't been able to find any documentation on them
U wrap mode and V wrap mode are used to determine how the texture is wrapped

alpha flags has the format: \[Opaque\],\[Transparent\],\[Additive\],\[Subtractive\]
if Opaque is 1 then no other flags will be set and the texture will be rendered opaquely
if only Transparent is set then the texture should be rendered with alpha blending
if Transparent and Additive are set then the texture should be rendered with additive blending
if Transparent and Subtractive are set then the texture should be rendered with subtractive blending

Wrap Mode can be one of the following:
    Repeat, Clamp, RegionClamp, RegionRepeat
    Repeat will repeat the texture
    Clamp will clamp the texture
    Region* will not do anything different from the other two modes unless you are using non-sliced textures, but I have not documented the output format for that

There is also a new folder named `ImporterScripts` which contains a python script used to import the exported map models and textures into blender (only works with pre-sliced)
as well as a unity script and 3 shaders to be used for importing the map models and textures into unity, this works with both pre-sliced and non-sliced textures but it is still recommended to use pre-sliced textures
