# Sbox Package Extractor

A simple CLI tool for extracting `.cs` and `.razor` files from s&box `.cll` files. It supports compiling them into a DLL for use in your own mods, etc., without using reflection.

## Usage

Run the executable via command line. By default, it scans the current working directory.

```cmd
PackageExtractor.exe
```

You can also pass a specific path to your s&box installation or project folder as an argument:

```cmd
PackageExtractor.exe "C:\Program Files (x86)\Steam\steamapps\common\sbox"
```

## Notes

- Extracted packages are saved in the `extracted/` directory next to the tool's executable.
- Compiled DLLs are saved in the `extracted/game/dll/`.