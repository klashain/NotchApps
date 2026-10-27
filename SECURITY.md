# Security policy

NotchApps runs with administrator rights (through a scheduled task) and NotchPilot writes to the HP BIOS, so security issues are taken seriously.

## Supported versions

Only the latest release receives fixes.

## Reporting a vulnerability

Please **do not open a public issue** for security problems. Instead, use GitHub's [private vulnerability reporting](https://github.com/klashain/NotchApps/security/advisories/new).

Include the affected app and version, steps to reproduce, and the impact (for example, privilege escalation through the admin task or install folder).

## Design notes

- The admin task only runs the exe in the install folder; the installer locks that folder to administrators so a non-admin process cannot replace the exe and gain elevation.
- The task XML is held open while `schtasks` reads it, so it cannot be swapped in between.
- Fan commands are only sent when the user picks a speed; fans return to BIOS auto mode on exit, on error, and above the safety temperature.
