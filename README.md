Checkers API Project
This project provides a robust ASP.NET Core Web API for the game of checkers, integrating with the external Chinook (Kingsrow/Cake) engine for move suggestions and validation.
System Requirements
.NET 9 SDK (ensure this is installed on the hosting machine)
Visual Studio 2022 (for development environment)
The Kingsrow/Cake engine files (.exe, .dll, .wld, etc.)
Deployment and Setup Instructions
Before running the application, you must configure the paths to the external engine files and the database files.
1. Place Engine and Database Files
Create the necessary folders on your system (e.g., C:\Checkers\Engine\ and C:\Checkers\DB\) and place the required files there:
Engine Folder (C:\Checkers\Engine\):
KingsrowWorker.exe (The .NET wrapper process)
KingsrowWorker.dll
KingsrowWorker.deps.json
KingsrowWorker.runtimeconfig.json
cake.ini
cake_189f.dll (The core engine library)
(Note: Ensure the cake.ini file's internal egdb_path points to your DB folder.)
Database Folder (C:\Checkers\DB\):
All required database files (e.g., .wld, .tbl files).
2. Configure appsettings.json
Update the paths in the appsettings.json file of the main CheckersBot project to match the locations where you placed the files in Step 1.
-------------------
json
{
  "Engine": {
    "Type": "chinook",
    // Update this path to your exact location
    "Path": "C:\\Checkers\\Engine\\KingsrowWorker.exe", 
    "Workers": 2,
    // Update this path to your exact location
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
---------------------
How to Run
Open the solution in Visual Studio 2022.
Ensure the configuration is set to Debug or Release (as used during development).
Run the project (F5 or click the play button).
The application will start, launch two KingsrowWorker.exe processes in the background, and open the Swagger UI in your browser.
--------------------
API Endpoints
The API includes the following endpoints documented via Swagger:
--------------------
Endpoint/	Description/	Status Codes:
POST /api/Checkers/suggest	Get the best move suggestion based on a position and level.	200 OK, 422 Invalid PDN, 504 Timeout
POST /api/Checkers/validate	Check if a specific move is legal in a position.	200 OK
GET /healthz	Health check endpoint to confirm workers are running.	200 OK
--------------------
Example Request (suggest)
json
{
  "gameId": "checkers-8x8",
  "state": { "notation": "PDN", "position": "B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16" },
  "level": "weak",
  "limits": { "maxDepth": 12, "softTimeMs": 250, "hardTimeMs": 1200 }
}
