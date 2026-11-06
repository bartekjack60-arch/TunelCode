# Rozwój AutoTunel MB 0.8

Cel: jeden EXE zawiera pakiety potrzebne do obsługi USB/IP. Windows instaluje
komponenty automatycznie z zasobów EXE, z uprawnieniami administratora.
Protokół AMB4, TLS i zasady własności urządzeń pozostają zgodne z 0.5.

## Budowanie

Wymagane .NET SDK 8 i reference assemblies .NET Framework 4.8 z NuGet
(Microsoft.NETFramework.ReferenceAssemblies 1.0.3).
Windows: `powershell -File source/BUILD.ps1`. Linux: `bash tools/build.sh`.
W bieżącym środowisku:

```bash
export DOTNET_ROOT=/workspace/.tools/dotnet-8
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME=/workspace/.cache/dotnet-cli
export NUGET_PACKAGES=/workspace/.cache/nuget/packages
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
cd /workspace/TunelCode
bash tools/build.sh
```

Wynik: `source/dist/AutoTunel/AutoTunel.exe`, net48 x64, bez DLL/config obok EXE.
Pomoc, licencje i oba instalatory są zasobami EXE. Pełny ZIP zawiera `vendor/usb`.
Skrypty sprawdzają SHA-256 pakietów przed kompilacją. Do przywrócenia brakujących
pakietów na komputerze dewelopera: `python3 tools/fetch-usb.py`.
Aplikacja nie wykonuje pobierania.

Testy: `dotnet run --project source/tests/Tests.csproj -c Release`.
Wybrany zakres: dopisz `-- "USB setup"`. Brak dopasowań jest błędem.
Weryfikacja rzeczywistego EXE:

```bash
dotnet run --project source/tests/Tests.csproj -c Release -- \
  --verify-bundle "$PWD/source/dist/AutoTunel/AutoTunel.exe"
```

Sprawdzane są wersja, SHA-256 obu osadzonych pakietów, aktualność instrukcji,
obecność licencji i brak zewnętrznych DLL/config. Tryb ten nie instaluje sterowników.

## Przygotowanie USB

- `UsbSetup.cs`: polityka przygotowania, metadane, integralność, kody instalatora,
  anulowanie przed instalacją i wymagany restart.
- `WindowsUsbSetup.cs`: globalna blokada, chronione pliki robocze, instalatory,
  wykrywanie programów i usługi.
- `MainForm.cs`: serwer jest przygotowywany przed listą USB, klient przed
  połączeniem USB. Przycisk „Przygotuj USB” pozwala przygotować je wcześniej.
- `vendor/usb/packages.json`: źródło nazw plików, zasobów i sum SHA-256.

Wbudowano niezmodyfikowane oficjalne usbipd-win 5.3.0 x64 MSI i usbip-win2
0.9.8.1 x64 EXE. SHA-256 są zgodne z `digest` oficjalnych wydań GitHub.
Nie modyfikujemy sterowników ani nie włączamy trybu testowego Windows.

MSI: systemowy msiexec, `/qn /norestart REBOOT=ReallySuppress`,
`ADDLOCAL=Server,Drivers`, pełny dziennik. Klient: `/SP- /VERYSILENT
/SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=3010`, `main,client`, zadanie
`vcredist`. Nie wybieramy GUI, SDK ani PDB; runtime Visual C++ jest już
w instalatorze. Wyłączamy automatyczne zamykanie i restart innych aplikacji.

Plik jest wypisywany do nowego `%ProgramData%\AutoTunel-MB-USB-<losowy ID>`
z ACL tylko dla Administratorów i SYSTEM. SHA-256 jest sprawdzany przed
uruchomieniem. Uszkodzony plik jest usuwany. Globalny mutex serializuje
przygotowanie. Po zakończeniu instalatora kopia robocza jest usuwana;
`install.log` pozostaje. Ścieżkę zapisuje dziennik aplikacji.

Przed instalacją zapisujemy w HKLM (widok 64-bit)
`SOFTWARE\AutoTunel-MB\UsbSetup` wartości `ServerRestart` / `ClientRestart`.
Zawierają czas ostatniego startu Windows z WMI `LastBootUpTime`. Znacznik
pozostaje przy wymaganym restarcie lub niepotwierdzonym trwającym instalatorze.
Definitywne niepowodzenie zakończonego instalatora lub jego uruchomienia
zwalnia rezerwację. Nowy start usuwa znacznik, następnie ponownie
sprawdzana jest gotowość. Po sukcesie serwera bez restartu znacznik usuwamy
dopiero po potwierdzeniu narzędzia i usługi.

3010 i 1641 oznaczają restart. Klient wymaga go także przy kodzie 0, bo jego
przypięty `setup.iss` deklaruje `AlwaysRestart=yes`. AutoTunel nigdy nie
wywołuje restartu. Po rozpoczęciu instalacji nie zabijamy instalatora przy
anulowaniu ani zamykaniu okna; czekamy do 15 minut. Po przekroczeniu limitu
instalator może nadal pracować, a znacznik blokuje kolejne przygotowanie.

Zgodna działająca instalacja jest używana bez reinstalacji. Przy automatycznym uruchomieniu istniejący plik
narzędzia o niezgodnej wersji lub niesprawny sterownik powoduje błąd z instrukcją
wywołania jawnej naprawy.
Nie uruchamiamy automatycznej aktualizacji mogącej przerwać inne sesje USB.
Ustawienia obu ról i dzienniki odzyskiwania z 0.4/0.5 są zachowane.

## Źródła kontraktów

- usbipd-win 5.3.0: `Installer/Product.wxs`, `Installer/Server.wxs`, `Drivers/README.md`:
  https://github.com/dorssel/usbipd-win/tree/v5.3.0
- usbip-win2 0.9.8.1: `userspace/innosetup/setup.iss`:
  https://github.com/vadimgrn/usbip-win2/blob/v.0.9.8.1/userspace/innosetup/setup.iss
- Inno Setup: https://jrsoftware.org/ishelp/topic_setupcmdline.htm
- Windows Installer: https://learn.microsoft.com/en-us/windows/win32/msi/standard-installer-command-line-options

Źródła obu komponentów są w `vendor/sources`, licencje i przekazana oferta
źródeł sterowników VirtualBox w `vendor/licenses`. Standardowa reguła zapory
usbipd jest częścią niezmodyfikowanego MSI. AutoTunel nie zmienia stanu zapory.

## Pakowanie i zakres weryfikacji

Po budowaniu odśwież `TESTY.txt`, następnie:

```bash
python3 tools/package.py --output /ścieżka/AutoTunel-MB-0.8.1.zip
```

ZIP zawiera jeden gotowy EXE, źródła, pakiety vendor, licencje i dokumentację.
`bin`, `obj`, `dist` i cache są pomijane. Skrypt tworzy manifest SHA-256 i sprawdza ZIP.
Sam EXE wystarcza do użycia na Windows z .NET Framework 4.8.

Testy Linux używają lokalnego TCP/TLS i kontrolowanych odpowiedzi CLI. Nowe
testy przygotowania używają atrap operacji systemowych. Nie potwierdzają
instalatorów, WMI, PnP, restartu ani GUI Windows. Próba: `WINDOWS-TEST.md`.

## Interfejs i ikona 0.7

Interfejs korzysta wyłącznie z WinForms/GDI+ oraz systemowej czcionki Segoe UI.
Grafitowy motyw i własne kontrolki są w source/src/Ui.cs. Nie wymagają bibliotek
UI ani plików obok EXE. Ikona source/AutoTunel.ico ma rozmiary 16, 24, 32, 48,
64, 128 i 256 pikseli i trafia do natywnych zasobów Windows EXE. Ten sam plik
jest zasobem zarządzanym AutoTunel.Brand.ico dla okien, a logo
source/AutoTunel.png jest zasobem AutoTunel.Brand.png interfejsu.

Natywną ikonę i format EXE sprawdza:

```bash
python3 tools/verify-icon.py source/dist/AutoTunel/AutoTunel.exe source/AutoTunel.ico
```

Weryfikator porównuje każdą klatkę ICO z RT_GROUP_ICON/RT_ICON wewnątrz PE
i sprawdza AMD64, PE32+ oraz podsystem Windows GUI.

## Naprawa instalacji i LTSC — 0.8

Serwer pozostaje oficjalnym usbipd-win 5.3.0; upstream deklaruje minimalny
Windows 10 1809 (17763). Klient usbip-win2 0.9.8.1 wymaga 1903 (18362).
Źródła: https://github.com/dorssel/usbipd-win/blob/v5.3.0/README.md oraz
https://github.com/vadimgrn/usbip-win2/blob/v.0.9.8.1/userspace/innosetup/setup.iss .
Nie modyfikowano podpisanych pakietów ani progów ich instalatorów.

Przed odczytem znacznika restartu wykonywane jest sprawdzenie rzeczywistego
Windows i natywnej architektury. Rozpoznane definitywne błędy zakończonego
instalatora i uruchomienia zwalniają rezerwację restartu. Przekroczenie czasu
pracującego instalatora pozostawia rezerwację; procesu nie zabijamy.

Naprawa jest wyłącznie jawnie wywoływana przyciskiem Napraw USB. Sprawdzamy
własny dziennik, aktywne połączenia USB/IP i VirtualBox oraz wersje CLI/MSI.
Dla zarejestrowanej wersji 5.3 używamy REINSTALL=ALL REINSTALLMODE=amus
z normalnym /i, ADDLOCAL=Server,Drivers i blokadą restartu. Dla rozpoznanej
starszej zarejestrowanej wersji normalne /i uruchamia oficjalny MajorUpgrade.
Dla pozostałego pliku narzędzia bez rejestracji MSI wykonywana jest instalacja
tylko wtedy, gdy odpowiada wersji wbudowanej. Nowsze/nierozpoznane wersje
i zajęte urządzenia powodują odmowę. Automatyczny start nie aktualizuje obcych wersji.

Zasady MSI: https://learn.microsoft.com/en-us/windows/win32/msi/reinstallmode
oraz https://learn.microsoft.com/en-us/windows/win32/msi/reinstall .
Trwałe dzienniki diagnostyczne są oddzielone od sekretów i kodów sesji.
Odzyskiwanie własnego USB nie uruchamia przygotowania/naprawy i nie zależy
od znacznika instalacji. Testy Windows pozostają konieczne; screenshot
Windows Enterprise LTSC i ogólny opis błędu nie wskazują jednoznacznie
przyczyny awarii konkretnego sterownika.

Przed startem MSI ponownie sprawdzamy brak klientów/VirtualBox/własnego zapisu,
również dla świeżej instalacji. Wykryta już zarejestrowana instalacja z brakującym
CLI nie jest automatycznie aktualizowana: wymaga jawnej naprawy. Autorytatywna
ścieżka z Windows Installer ma pierwszeństwo przed starym plikiem ustawień,
także gdy plik narzędzia z zarejestrowanej instalacji wymaga odtworzenia.
Przy starszym lub brakującym CLI i działającej usłudze naprawa jest blokowana
do zakończenia sesji i zatrzymania USBIP Device Host w Usługach Windows.

## Poprawka 0.8.1 — składnia msiexec / błąd 1639

Windows Installer wymaga własnej składni polecenia. Nie wolno traktować całego
PROPERTY=VALUE jak argumentu C/C++ i obejmować cudzysłowami nazwy właściwości.
W wersji 0.8 ten sam ogólny mechanizm obejmował cudzysłowami również /i, /qn
i całe REBOOT=ReallySuppress, co jest nieprawidłowe dla MSI.

UsbInstallCommand.CommandLine tworzy teraz końcowy zapis. Opcje i stałe
właściwości pozostają niecytowane, ścieżki MSI i dziennika są cytowane.
Ścieżki zawierające cudzysłowy lub znaki sterujące są odrzucane przed startem.
Dokładnie ten sam string trafia do ProcessStartInfo.Arguments i diagnostyki.
Runner USB/IP i format argumentów Inno Setup nie są zmienione.

Nowy test regresji końcowego polecenia zawiódł na poprzednim formatowaniu,
następnie przeszedł po poprawce. Obejmuje świeżą instalację i naprawę,
ścieżki ze spacjami, polskimi literami i znakiem równości. Kod 1639 jest
uwzględniony w teście zachowania błędu bez fikcyjnego restartu.

Źródła: https://learn.microsoft.com/en-us/windows/win32/msi/command-line-options
oraz https://learn.microsoft.com/en-us/windows/win32/msi/error-codes .
Test formatowania nie jest uruchomieniem natywnego msiexec na Windows;
instalacja na komputerze użytkownika pozostaje do sprawdzenia.
