# Installs wasmtime on Windows using the official installer script.

Write-Host "Downloading and installing wasmtime..."
try {
    $installScript = Invoke-WebRequest -Uri "https://wasmtime.dev/install.ps1" -UseBasicParsing
    Invoke-Expression -Command $installScript.Content
    Write-Host "Wasmtime installation script executed."
    Write-Host "Please follow the instructions to add wasmtime to your PATH."
} catch {
    Write-Error "Failed to download or execute the wasmtime installation script. Please visit https://wasmtime.dev/ for manual installation instructions."
}
