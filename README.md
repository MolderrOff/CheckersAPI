# Checkers API Project

This project provides a robust **ASP.NET Core Web API** for the game of checkers, integrating with the external **Chinook (Kingsrow/Cake)** engine for move suggestions and validation.

## System Requirements

*   **.NET 9 SDK** (ensure this is installed on the hosting machine)
*   **Visual Studio 2022** (for development environment)
*   **Kingsrow/Cake engine files** (`.exe`, `.dll`, `.wld`, etc.)

## Deployment and Setup Instructions

Before running the application, you must configure the paths to the external engine files and the database files.

### 1. Place Engine and Database Files
Create the necessary folders on your system (e.g., `C:\Checkers\Engine\` and `C:\Checkers\DB\`) and place the required files there:

**Engine Folder (`C:\Checkers\Engine`):**
*   `KingsrowWorker.exe` (The .NET wrapper process)
*   `KingsrowWorker.dll`, `KingsrowWorker.deps.json`, `KingsrowWorker.runtimeconfig.json`
*   `cake.ini`, `cake_189f.dll` (The core engine library)
*   *Note: Ensure the `cake.ini` file's internal `egdb_path` points to your DB folder.*

**Database Folder (`C:\Checkers\DB`):**
*   All required database files (e.g., `.wld`, `.tbl` files).
*   *Download link:* [edgilbert.org - Kingsrow English](http://edgilbert.org). You will need the files titled **"Kingsrow English 2 through 8 pieces WLD"**.

### 2. Configure `appsettings.json`
Update the paths in the `appsettings.json` file of the main **CheckersBot** project to match the locations from Step 1.

```json
{
  "Engine": {
    "Type": "chinook",
    "Path": "C:\\Checkers\\Engine\\KingsrowWorker.exe",
    "Workers": 2,
    "Databases": "C:\\Checkers\\DB\\"
  },
  "Cache": {
    "Capacity": 20000,
    "TtlMinutes": 15
  },
  "Limits": {
    "DefaultSoftTimeMs": 300,
    "DefaultHardTimeMs": 1200
  }
}
