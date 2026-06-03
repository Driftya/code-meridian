$textExtensions = @('.cs','.csproj','.sln','.json','.ps1','.yml','.yaml','.md','.txt')
$files = Get-ChildItem -Path "c:\Projects\CodeMeridian" -Recurse -File | Where-Object {
    ($textExtensions -contains $_.Extension -or $_.Name -eq 'Dockerfile') -and
    $_.FullName -notmatch '\\obj\\' -and
    $_.FullName -notmatch '\\bin\\' -and
    $_.FullName -notmatch '\\.git\\'
}
$replaced = 0
foreach ($file in $files) {
    $content = [System.IO.File]::ReadAllText($file.FullName)
    if ($content -match 'CodeMeridian|CodeMeridian') {
        $new = $content -replace 'CodeMeridian', 'CodeMeridian' -replace 'CodeMeridian', 'codemeridian'
        [System.IO.File]::WriteAllText($file.FullName, $new, [System.Text.Encoding]::UTF8)
        $replaced++
    }
}
Write-Host "Step 1 done: content replaced in $replaced files" -ForegroundColor Green

# Step 2: Rename .csproj, .sln, and .cs files that have CodeMeridian in their name
$toRename = Get-ChildItem -Path "c:\Projects\CodeMeridian" -Recurse -File |
    Where-Object { $_.Name -match 'CodeMeridian' -and $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' }
foreach ($file in $toRename) {
    $newName = $file.Name -replace 'CodeMeridian', 'CodeMeridian'
    Rename-Item -Path $file.FullName -NewName $newName -Force
}
Write-Host "Step 2 done: renamed $($toRename.Count) files" -ForegroundColor Green

# Step 3: Rename the solution folder itself
# Note: VS Code workspace will need to be reopened after this
Write-Host "Step 3: Renaming project folder..." -ForegroundColor Cyan
Set-Location c:\Projects
Rename-Item -Path "c:\Projects\CodeMeridian" -NewName "CodeMeridian" -Force
Write-Host "Step 3 done: folder renamed to CodeMeridian" -ForegroundColor Green
Write-Host ""
Write-Host "All done! Reopen VS Code to: c:\Projects\CodeMeridian" -ForegroundColor Yellow
