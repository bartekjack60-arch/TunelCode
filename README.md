# AutoTunel MB 0.6.0 — USB w jednym EXE

**[Pobierz AutoTunel.exe](downloads/AutoTunel.exe?raw=true)**

[Pełna paczka ze źródłami, licencjami i raportem](downloads/AutoTunel-MB-0.6.0.zip?raw=true)

Na obu komputerach używaj tego samego EXE i wybierz rolę **Przy samochodzie**
lub **Diagnosta**. Pakiety USB/IP są wbudowane w program. AutoTunel przygotowuje
je automatycznie, bez osobnego pobierania i ręcznego uruchamiania instalatorów.

Przy samochodzie przygotowanie odbywa się przed wyświetleniem USB. U diagnosty
przed pierwszym połączeniem lub przez **Ustawienia USB → Przygotuj USB**.
Po pierwszym przygotowaniu klienta Windows wymaga restartu. Program pokaże
komunikat; komputer nie restartuje się samodzielnie. Kolejne uruchomienia
korzystają z gotowej obsługi. Działające zgodne instalacje z 0.5 są zachowane.
Inna lub niesprawna istniejąca instalacja jest zgłaszana do sprawdzenia.

Wymagane Windows 10 (od 1903)/11 x64, .NET Framework 4.8, administrator,
RadminVPN i sterownik używanego interfejsu, np. Openporta. Zmiana dotyczy USB/IP;
ENET nadal wymaga Npcap/TAP. Pomoc i licencje są wbudowane w EXE.

Kompilacja: 0 ostrzeżeń, 0 błędów. **42 testy PASS** oraz kontrola SHA-256
obu oficjalnych pakietów zapisanych wewnątrz EXE. [Raport](TESTY.txt).
Instalatory, sterowniki, restart, GUI i fizyczny Openport/Xentry wymagają
jeszcze [próby na Windows](WINDOWS-TEST.md). EXE prototypu nie ma podpisu AutoTunel.

Oficjalny instalator usbipd-win tworzy standardową regułę zapory. Program
nie włącza ani nie wyłącza zapory. Bez zapory port USB/IP 3240 może być
dostępny bez kodu AutoTunel; korzystaj z zaufanej sieci.

Sumy kontrolne: [SHA256SUMS.txt](downloads/SHA256SUMS.txt).
Jeśli GitHub pokazuje 404, zaloguj się na konto z dostępem do prywatnego repozytorium.
Na stronie pliku można wybrać **Download raw file** lub **View raw**.
