.PHONY: build deps clean install

PKG_NAME = pushtray
PREFIX ?= /usr

MONO = /usr/bin/mono
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
	install -Dm644 build/dist/pushtray.exe $(PREFIX)/lib/$(PKG_NAME)/pushtray.exe
	install -D scripts/pushtray $(PREFIX)/bin/pushtray
	install -dm755 $(PREFIX)/share/$(PKG_NAME)
	cp -R share/icons $(PREFIX)/share/$(PKG_NAME)
