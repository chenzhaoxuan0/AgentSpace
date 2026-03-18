# AgentSpace Desktop

中文请查看[简体中文](README_zh_CN.md)
AgentSpace is a Windows desktop application that crops (masks) running applications and enables automated clipboard data transfer between them. It uses Win32 APIs and WPF to manipulate native windows.

## Features

- **Window Cropping**: Select an area on any active window to crop it. The cropped region is placed in a movable, resizable WPF container.
- **Native Synchronization**: While moving or resizing the container, the underlying native application is moved/resized synchronously.
- **Interactive**: The cropped application remains fully interactive (clickable, scrollable, typable).
- **Intent Routing**: Automatically copy selected text from a source container and paste it into a target window via visually routed keyboard automation.
- **DPI Support**: Supports scaled displays (e.g., 125%, 150%).

## Usage

Run `AgentSpace.exe` to start the application.

- `Ctrl + Shift + Space`: Start region selection. Click and drag to draw a box, then press `Enter` to crop.
- **Mouse Drag**: Drag the container's border to move it.
- **Resize**: Drag edges/corners to change the visible crop area.
- `Ctrl` + **Mouse Wheel**: Hover over the container to scale the physical native application.
- `ESC`: Close the container and restore the original window.
- `[ ➦ Route ]`: (Or custom hotkey) Open the target selection overlay. Use `Tab` to cycle targets, `Delete` to remove unwanted targets, and `Space` to execute the copy-paste transfer.

## Dependencies

- Environment: Windows 10/11
- Framework: .NET Core 3.0 (WPF)

## Development

1. Clone this repository.
2. Open `src/AgentSpace.sln` in Visual Studio or VS Code.
3. Build and run the `AgentSpace.App` project using `dotnet build` / `dotnet run`.
"# AgentSpace" 
