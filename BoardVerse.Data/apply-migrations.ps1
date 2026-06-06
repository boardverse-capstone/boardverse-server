# Run this from the repository root
# This will apply EF Core migrations to the database using BoardVerse.API as the startup project.

param(
    [string]$DatabaseUrl = $env:DATABASE_URL
)

if (-not $DatabaseUrl) {
    Write-Host "No DATABASE_URL found in environment. Using appsettings.json DefaultConnection."
}
else {
    Write-Host "Using DATABASE_URL from environment."
    $env:DATABASE_URL = $DatabaseUrl
}

# Ensure dotnet-ef is available (local manifest or global). If not installed, instruct the user.
try {
    dotnet tool run dotnet-ef --version | Out-Null
}
catch {
    Write-Host "dotnet-ef not found as local tool. Try: dotnet tool install --global dotnet-ef --version 8.0.10"
    exit 1
}

# Run migrations
cd ..\
Write-Host "Running migrations via dotnet ef database update --project BoardVerse.Data/BoardVerse.Data.csproj --startup-project BoardVerse.API/BoardVerse.API.csproj"
dotnet tool run dotnet-ef database update --project BoardVerse.Data/BoardVerse.Data.csproj --startup-project BoardVerse.API/BoardVerse.API.csproj
