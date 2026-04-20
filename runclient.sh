#!/bin/sh
dotnet run --project Content.Client --cvar log.level=0
read -p "Press enter to continue"
