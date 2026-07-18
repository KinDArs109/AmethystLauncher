@echo off
:: Stops the zapret bypass. winws.exe runs elevated, so stopping it needs elevation too —
:: the launcher runs this via ShellExecute verb=runas.
taskkill /IM winws.exe /F > nul 2>&1
:: Clean up the WinDivert driver service so it doesn't linger between sessions.
net stop "WinDivert" > nul 2>&1
sc delete "WinDivert" > nul 2>&1
