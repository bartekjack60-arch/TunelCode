# AutoTunel MB 0.4.0 — paczka do testów Windows

Ta gałąź służy do pobrania przygotowanej paczki. ZIP zawiera aplikacje
Client i Server, instrukcję, kod źródłowy i raport testów.

1. Otwórz `downloads/AutoTunel-MB-0.4.0-USBIP.zip`.
2. Użyj przycisku pobierania pliku w GitHub.
3. Rozpakuj ZIP i otwórz `INSTRUKCJA.html`.

Server uruchamia osoba przy samochodzie, a Client — diagnosta.
Dla USB wymagane są oddzielnie instalowane usbipd-win 5.3.0 na komputerze
przy samochodzie i usbip-win2 v.0.9.8.1 na komputerze diagnosty.
Oba komputery muszą mieć łączność przez RadminVPN.

Jest to prototyp: 23 testy transportu i logiki przeszły, oba pliki EXE
skompilowano dla Windows x64. Nie wykonano jeszcze testu na dwóch
fizycznych komputerach Windows z Openport 2.0 i Xentry. Pliki EXE
AutoTunel nie mają podpisu wydawcy.

Szczegóły instalacji, wymagań i testów sprzętowych są w plikach
`INSTRUKCJA.html`, `WINDOWS-TEST.md`, `TESTY.txt` oraz `THIRD-PARTY.txt` w ZIP.

Sumę SHA-256 paczki zapisano obok w `downloads/SHA256SUMS.txt`.
