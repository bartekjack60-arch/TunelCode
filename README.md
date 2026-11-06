# AutoTunel MB 0.8.1 — poprawka błędu instalatora 1639

**[Pobierz AutoTunel.exe](downloads/AutoTunel.exe?raw=true)**

Zamknij poprzedni program, uruchom nowy EXE i wybierz **Przy samochodzie**.
Jeśli przygotowanie nie ruszy automatycznie, użyj
**Ustawienia USB → Napraw USB**. W oknie powinien być numer **0.8.1**.

Poprawiono zapis polecenia Windows Installer: opcje i nazwy właściwości MSI
pozostają bez cudzysłowów, a cytowane są wyłącznie ścieżki MSI oraz dziennika.
Poprzedni mechanizm obejmował cudzysłowami także przełączniki i całe
REBOOT=ReallySuppress, co jest błędne w składni Windows Installer.
Kod 1639 to ERROR_INVALID_COMMAND_LINE. Obecnie log zawiera identyczne
polecenie jak to przekazywane do ProcessStartInfo.Arguments.

Nowy test końcowego polecenia zawiódł na starym formatowaniu i przeszedł
po poprawce. Sprawdzono instalację i naprawę, ścieżki ze spacjami i polskimi
znakami oraz odrzucanie nieprawidłowych znaków przed startem procesu.
Kompilacja: **0 ostrzeżeń, 0 błędów; 54 testy PASS**.
[Raport](TESTY.txt) • [Procedura sprawdzenia na Windows](WINDOWS-TEST.md)

Testy wykonano w Linux; nie uruchomiono natywnego msiexec ani fizycznego USB
na komputerze użytkownika. Trzeba ponowić instalację na Windows.
Oryginalne instalatory i ich podpisy pozostają bez zmian.

Nadal jeden EXE zawiera obie role, pakiety USB/IP, pomoc i ikonę.
Serwer wymaga Windows 10 1809/x64 lub nowszego, Diagnosta 1903/x64 lub nowszego.
Wymagane są .NET Framework 4.8, administrator, RadminVPN i sterownik interfejsu.
Wymagany restart jest sygnalizowany; komputer nie restartuje się samodzielnie.
Diagnostyka USB otwiera zachowane dzienniki instalacji.

[Pełna paczka ze źródłami i licencjami](downloads/AutoTunel-MB-0.8.1.zip?raw=true)
• [Sumy SHA-256](downloads/SHA256SUMS.txt)

Źródła składni: [Windows Installer](https://learn.microsoft.com/en-us/windows/win32/msi/command-line-options),
[kody błędów](https://learn.microsoft.com/en-us/windows/win32/msi/error-codes).

Na stronie pliku można wybrać **Download raw file** lub **View raw**.

## Kod źródłowy i kompilacja

Główna gałąź zawiera komplet źródeł wydania 0.8.1, pakiety USB i licencje.
Gotowy pojedynczy EXE znajduje się w `downloads/AutoTunel.exe`.

- Windows z .NET SDK 8: `powershell -File source/BUILD.ps1`.
- Linux z .NET SDK 8 i Pythonem: `bash tools/build.sh`.
- Wynik kompilacji: `source/dist/AutoTunel/AutoTunel.exe`.

[Dokumentacja deweloperska](DEVELOPMENT.md) • [Instrukcja](INSTRUKCJA.html)

Źródła i pliki vendor są zgodne z opublikowaną paczką 0.8.1.
