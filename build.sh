#!/usr/bin/bash
if [ "$OS" == "Windows_NT" ]; then
  ./packages/FAKE/tools/FAKE.exe "$@"
else
  mono ./packages/FAKE/tools/FAKE.exe "$@"
fi