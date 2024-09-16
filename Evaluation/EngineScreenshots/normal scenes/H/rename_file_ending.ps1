# Rename-PngToJpg.ps1

# Get all .png files in the current directory
$pngFiles = Get-ChildItem -Path . -Filter *.png

# Loop through each .png file
foreach ($file in $pngFiles) {
    # Create the new file name by replacing .png with .jpg
    $newName = $file.Name -replace '\.png$', '.jpg'
    
    # Rename the file
    Rename-Item -Path $file.FullName -NewName $newName
    
    # Output a message for each renamed file
    Write-Host "Renamed: $($file.Name) to $newName"
}

Write-Host "Renaming complete."