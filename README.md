![NamespacePath](https://img.shields.io/badge/NamespacePath-Namespace%20Refactoring-blue?style=for-the-badge&logo=unity)

NamespacePath - Namespace Refactoring Tool for Unity
===

[![Unity](https://img.shields.io/badge/Unity-2020.3+-black.svg)](https://unity3d.com/pt/get-unity/download/archive)
[![MIT License](https://img.shields.io/badge/License-MIT-green.svg)](https://choosealicense.com/licenses/mit/)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-blueviolet)](https://makeapullrequest.com)

A powerful Unity Editor tool for managing and refactoring C# namespaces based on folder structure. Automatically suggests namespaces, updates using statements, and cleans up unused references.

## ✨ Features

- 🔍 **Folder-based namespace suggestions** - Automatically generates namespace based on folder structure
- 🏷️ **Customizable prefix** - Add your project's root namespace as prefix (e.g., `MyProject.Core.Utils`)
- 📦 **Batch processing** - Select multiple scripts and apply changes at once
- 👀 **Affected files preview** - See which files will be modified before applying changes
- 🔗 **Smart reference updates** - Updates `using` statements and fully-qualified references
- 🎯 **Type-aware detection** - Only updates files that actually use the types being moved
- 🧹 **Unused using cleanup** - Removes `using` statements that are no longer needed
- 🚫 **Duplicate removal** - Automatically removes duplicate `using` statements
- ⚠️ **Conflict detection** - Warns when namespace segment matches a type name
- 🔎 **Filters** - Filter by "needs change", "no namespace", or search by name

## 📦 Installation

### Via Git URL (Package Manager)

1. Open Package Manager in Unity (`Window > Package Manager`)
2. Click the `+` button and select `Add package from git URL...`
3. Enter the following URL:

```bash
https://github.com/xavierarpa/NamespacePath.git
```

### Manual Installation

1. Download or clone the repository
2. Copy the `namespacePath` folder into your project's `Assets/Plugins/` folder

## 🚀 Quick Start

### Opening the Tool

Go to `Tools > Namespace Path` in the Unity menu bar.

### Basic Usage

1. **Set Source Folder** - Drag a folder from your Project window to the "Source Folder" field
2. **Set Namespace Prefix** - Enter your project's root namespace (e.g., `MyProject`)
3. **Click "Scan Scripts"** - The tool will analyze all C# files in the folder
4. **Review suggestions** - Each script shows its current namespace and suggested namespace
5. **Select scripts** - Check the scripts you want to modify
6. **Preview affected files** - Click "View Affected" to see what files will be updated
7. **Apply changes** - Click "Apply Changes" to execute the refactoring

### Configuration Options

| Option | Description |
|--------|-------------|
| **Source Folder** | Folder containing the scripts to analyze |
| **References Folder** | Folder to search for `using` references (defaults to `Assets/`) |
| **Namespace Prefix** | Prefix added to all suggested namespaces |
| **Use Source as Root** | If enabled, namespace starts from source folder name |

### Example

Given this folder structure:
```
Assets/Scripts/Runtime/Core/
├── Utils/
│   └── Utils.cs          (namespace: MyProject.Core)
├── Services/
│   └── GameService.cs    (namespace: MyProject.Core)
└── Data/
    └── PlayerData.cs     (namespace: MyProject.Core)
```

With **Source Folder** = `Core` and **Prefix** = `MyProject`:

| File | Current | Suggested |
|------|---------|-----------|
| Utils.cs | MyProject.Core | MyProject.Core.Utils |
| GameService.cs | MyProject.Core | MyProject.Core.Services |
| PlayerData.cs | MyProject.Core | MyProject.Core.Data |

## ⚠️ Conflict Detection

The tool warns you when a namespace segment matches a type name in the same file. For example:

```csharp
// ⚠️ Warning: namespace ends with 'Utils' which matches the class name
namespace MyProject.Core.Utils
{
    public class Utils { }  // This causes CS0101 error
}
```

## 🔄 How It Works

1. **Scans** all `.cs` files in the source folder
2. **Extracts** current namespace and type names (classes, structs, interfaces, enums)
3. **Generates** suggested namespace based on folder path
4. **Detects** which files use the types being moved
5. **Updates** the namespace declaration in source files
6. **Adds** new `using` statements where needed
7. **Cleans up** unused `using` statements

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🤝 Contributing

Contributions are welcome! Please read [CONTRIBUTING.md](CONTRIBUTING.md) for details.

## 📧 Contact

- **Author**: Xavier Arpa ([@xavierarpa](https://github.com/xavierarpa))
- **Issues**: [GitHub Issues](https://github.com/xavierarpa/NamespacePath/issues)
