#!/bin/bash

node --inspect-port=127.0.0.1:9229 ui/index.js &

dotnet Gradinware.dll
