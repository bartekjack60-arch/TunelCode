# AutoTunel MB 0.5.0 — jeden EXE

**[Pobierz AutoTunel.exe](downloads/AutoTunel.exe?raw=true)**

[Pełna paczka ze źródłami i raportem testów](downloads/AutoTunel-MB-0.5.0.zip?raw=true)

Na obu komputerach używaj tego samego EXE. Przy starcie wybierz:

- **Przy samochodzie (Server)** — komputer klienta z podłączonym interfejsem.
- **Diagnosta (Client)** — Twój komputer z programem diagnostycznym.

Wersja 0.5 ma nowy układ okna, wybór roli i wspólne ustawienia.
Instrukcja jest wbudowana w program. Nie wymaga osobnego pliku `.exe.config`.
Ścieżki narzędzi obu ról są zapisane w `%LOCALAPPDATA%\AutoTunel-MB\settings.json`.

Zgodnie z przyjętą konfiguracją program nie sprawdza stanu Zapory Windows
ani nie tworzy i nie usuwa jej reguł. Działa przy założeniu wyłączonej zapory.
Nie zmienia stanu zapory. Bez zapory usługa usbipd-win na TCP 3240 może być
osiągalna z innych komputerów, poza kodem i tunelem TLS AutoTunel.
Wymagana jest zaufana sieć; sam RadminVPN nie blokuje innych kart sieciowych.

Wymagania: Windows 10/11 x64, .NET Framework 4.8, RadminVPN oraz oddzielnie
zainstalowane komponenty USB/IP: usbipd-win 5.3.0 przy samochodzie,
usbip-win2 0.9.8.1 u diagnosty. Sterowniki nie są dołączone do EXE.

Kompilacja: 0 ostrzeżeń, 0 błędów. Przeszło 29 testów transportu i logiki.
Sprawdzono osadzenie instrukcji, biblioteki systemowe i brak plików towarzyszących EXE.
Nie przeprowadzono jeszcze próby GUI ani rzeczywistego USB/ENET, Openporta i Xentry na Windows.
Plik EXE prototypu nie ma podpisu wydawcy.

Jeśli GitHub pokazuje 404, zaloguj się na konto mające dostęp do repozytorium.
Na stronie pliku możesz użyć **Download raw file** lub **View raw**.
Sumy kontrolne paczek: `downloads/SHA256SUMS.txt`.
