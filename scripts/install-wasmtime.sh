#!/bin/bash
# Installs wasmtime on Linux or macOS using the official installer script.

echo "Downloading and installing wasmtime..."
curl https://wasmtime.dev/install.sh -sSf | bash

if [ $? -eq 0 ]; then
    echo "Wasmtime installation script executed successfully."
    echo "Please follow the instructions to configure your shell environment."
else
    echo "Failed to download or execute the wasmtime installation script. Please visit https://wasmtime.dev/ for manual installation instructions."
fi
