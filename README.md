# UnityZedEditor
Code editor integration for supporting Zed as code editor for Unity

[![GitHub license](https://img.shields.io/github/license/nuskey8/UnityZedEditor)](./LICENSE)
![Unity 2021.3+](https://img.shields.io/badge/unity-2021.3+-000.svg)

![image](docs/preferences.png)

English | [日本語](README.ja.md)

## Overview

UnityZedEditor is an editor extension that allows you to use Zed in Unity. In addition to enabling Zed to be set as the External Script Editor, it provides features to automatically generate initial configurations for `.slnx`/`.csproj` and `.zed/settings.json`.

## Requirements

- Unity 2021.3 or higher
- `com.unity.ide.visualstudio` 2.0.26 or higher
  - This is a dependency required for implementation reasons. Most of the functionality is implemented using the public API of `Microsoft.Unity.VisualStudio.Editor`.

## Installation

### Unity

Open `Window > Package Management > Package Manager`, click `"+" > Install package from git URL...`, and enter the following URL.

```
https://github.com/nuskey8/UnityZedEditor.git
```

Alternatively, open `Packages/manifest.json` and add the following line to `dependencies`.

```json
"com.nuskey8.ide.zed": "https://github.com/nuskey8/UnityZedEditor.git?path=Assets/UnityZedEditor",
```

### Zed CLI

The Zed CLI is required to use UnityZedEditor. Please follow the steps below to complete the setup.

https://zed.dev/docs/reference/cli

## Features

Adding UnityZedEditor to your project allows you to set Zed as your External Script Editor. This enables the following features:

- Launching Zed when opening C# scripts in the Unity editor
- Opening the target file in Zed when double-clicking a Console log
- Generating `.zed/settings.json` and `.zed/debug.json` for Unity projects upon first launch
- Automatic generation or manual regeneration of `.csproj`/`.slnx`

## Debugger Connection

The current C# extension for Zed does not support debugging, but you can use a debugger with [DotRush](https://github.com/JaneySprings/DotRush). UnityZedEditor generates a `debug.json` configuration that can be used with DotRush.

> [!NOTE]
> The current DotRush debugger has an issue that prevents it from working correctly with Zed and Unity. Until the issue is fixed upstream, we recommend using [this fork with the fix](https://github.com/nuskey8/DotRush). See [this issue](https://github.com/JaneySprings/DotRush/issues/200) for details.

## License

[MIT](LICENSE)
