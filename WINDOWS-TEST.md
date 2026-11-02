# Próba AutoTunel 0.6 na Windows

Wymagane Windows 10 (od 1903)/11 x64, .NET Framework 4.8 i administrator.
Testy Linux nie uruchamiają instalatorów ani sterowników Windows.

## Czysty system — bez ręcznej instalacji programów USB

1. Użyj czystego systemu/VM z punktem przywracania, bez usbipd-win i usbip-win2.
   Skopiuj tylko `AutoTunel.exe` do pustego katalogu, także ze spacjami.
2. Sprawdź start obu ról bez config/DLL obok EXE. Pomoc i **Ustawienia USB →
   Licencje składników USB** mają działać bez Internetu.
3. Wyłącz dostęp do Internetu na czas przygotowania USB. Wybierz **Przy
   samochodzie**. USB ma przygotować się automatycznie, bez pobierania
   i uruchamiania oddzielnego instalatora przez użytkownika.
4. Sprawdź dziennik, kod instalatora i gotowość listy USB po ewentualnym restarcie.
   Usługa `usbipd` ma działać. Sprawdź standardową regułę usbipd i stan zapory.
5. Na drugim czystym systemie wybierz **Diagnosta**. Przygotuj USB przez
   **Ustawienia USB → Przygotuj USB** lub wklej kod i kliknij Połącz.
   Nie powinno być osobnego kreatora. Klient instaluje sterownik i dołączony
   runtime Visual C++, bez GUI/SDK/PDB projektu USBip.
6. Klient ma wymagać restartu. **Żaden komputer nie może zrestartować się sam.**
   Zamknięcie i otwarcie AutoTunel w tej samej sesji ma zachować komunikat.
7. Wybierz w Windows **Uruchom ponownie** i otwórz AutoTunel. Sprawdź gotowość
   USB i brak kolejnej instalacji. Powtórz uruchomienie.
8. Uruchom AutoTunel w drugiej sesji użytkownika w trakcie przygotowania.
   Dwa instalatory nie mogą pracować jednocześnie. Zamknięcie okna w trakcie
   przygotowania ma zaczekać na instalator, bez zabijania procesu.
9. Sprawdź zgodne instalacje 5.3.0 / 0.9.8.1 z wersji 0.5: bez reinstalacji,
   z zachowaniem ścieżek i dzienników własności USB.
10. Sprawdź błąd instalacji, inną istniejącą wersję i brak gotowości sterownika:
    komunikat błędu, zachowany dziennik i brak fałszywego potwierdzenia USB.
    Nie powinna wystartować automatyczna aktualizacja obcej instalacji.

## Rzeczywiste USB — Openport / Xentry PassThru

1. Połącz komputery przez RadminVPN. Zachowaj sterownik Openporta u diagnosty;
   zwolnij interfejs w VirtualHere i innych aplikacjach.
2. Przy samochodzie wybierz USB i interfejs, kliknij **Udostępnij interfejs**.
   Kod ma pojawić się po potwierdzeniu eksportu i uruchomieniu nasłuchu.
3. Przekaż kod prywatnie i połącz się u diagnosty. Nie dodawaj kodu do raportu.
4. Sprawdź urządzenie i jego sterownik w Windows, następnie wybór Openporta,
   identyfikację pojazdu i odczyt w Xentry PassThru. Zanotuj wersje i wyniki.
5. Kliknij **Rozłącz**, sprawdź zwolnienie i kolejną sesję z nowym kodem.
   Osobno sprawdź odpięcie USB i utratę RadminVPN podczas odczytu.
6. Po awarii użyj **Zwolnij USB po awarii**. Inne urządzenia mają pozostać bez zmian.

Przy wyłączonej zaporze TCP 3240 może być dostępny z innych kart; kod i TLS
chronią tunel AutoTunel, nie bezpośredni port usbipd. Testuj w zaufanej sieci
bez przekierowania tego portu na routerze.

Sprawdź GUI przy 100%, 125%, 150% i 200%, szczególnie komunikaty przygotowania,
restartu i błędów. ENET wymaga osobnej próby Npcap/TAP, adresacji i ENET/DoIP.
SM2/SM3 Pro, Launch, VCX SE, DTS Monaco i Vediamo pozostają niepotwierdzone.
Próba dotyczy identyfikacji i odczytu, bez programowania ECU.
