.PHONY: build deps clean install

PKG_NAME = pushtray
PREFIX ?= /usr
BIN_DIR = $(PREFIX)/bin
SHARE_DIR = $(PREFIX)/share

MONO = /usr/bin/mono
MONO_INSTALL_DIR = $(PREFIX)/lib/mono
FAKE = packages/FAKE/tools/FAKE.exe
PAKET = .paket/paket.exe
PAKET_BOOTSTRAP = .paket/paket.bootstrapper.exe

all: build

build: deps
	$(MONO) $(FAKE) release config=Release

deps:
	@test -f $(PAKET_BOOTSTRAP) || { echo "$(PAKET_BOOTSTRAP) not found, exiting..."; exit 1; }
	@test -f $(PAKET) || { echo "Downloading paket..."; $(MONO) $(PAKET_BOOTSTRAP); }
	$(MONO) $(PAKET) install

clean:
	$(MONO) $(FAKE) clean

install:
	install -D -m644 build/dist/pushtray.exe $(MONO_INSTALL_DIR)/$(PKG_NAME)/pushtray.exe
	install -D scripts/pushtray $(BIN_DIR)/pushtray
	mkdir -p $(SHARE_DIR)/$(PKG_NAME)
	cp -R share/icons $(SHARE_DIR)/$(PKG_NAME)
