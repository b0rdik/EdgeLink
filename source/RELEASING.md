# Публикация обновления

1. Измените корневой `VERSION` (стабильный `major.minor.patch`) и `<Version>` в `source/windows/EdgeLink.csproj`.
2. Соберите Mac: `bash source/build-mac.sh`. Соберите Windows на Windows с .NET 10: `powershell -ExecutionPolicy Bypass -File source/build-windows.ps1`. Windows использует WinUI 3 / Windows App SDK; XAML-компилятор запускается на Windows. Передайте папку `Windows/` на Mac для упаковки. Сборка Windows самодостаточная: EXE включает .NET, Windows App SDK, PRI и ресурсы; отдельная установка Windows App Runtime не нужна. При первом запуске содержимое извлекается в служебную папку .NET в `%TEMP%`.
3. Выполните `bash source/run-checks.sh` на Mac с .NET SDK. Проверьте реальные ввод и звук на обоих компьютерах. Скопируйте исходники в Git, закоммитьте и отправьте изменения.
4. На Mac запустите `python3 source/package-release.py`. Скрипт проверяет версии, PE x64 и подпись Mac, создаёт два архивных файла в `releases/`.
5. Создайте draft GitHub Release для тега `v<VERSION>` в `b0rdik/EdgeLink`, прикрепите **оба** ZIP и `SHA256.txt`, затем опубликуйте его как стабильный релиз. Имена файлов обязательны: `EdgeLink-macos-arm64.zip`, `EdgeLink-windows-x64.zip`.
6. Проверьте API `repos/b0rdik/EdgeLink/releases/latest`: оба assets должны содержать `digest: sha256:<hash>`, совпадающий с локальным SHA256. Приложения не принимают архив без digest.

Пример с GitHub CLI после входа в аккаунт с доступом к репозиторию:

```sh
gh release create v0.3.0 releases/EdgeLink-macos-arm64.zip releases/EdgeLink-windows-x64.zip releases/SHA256.txt --repo b0rdik/EdgeLink --draft --title 'EdgeLink 0.3.0' --notes-file RELEASE-NOTES.md
gh release edit v0.3.0 --repo b0rdik/EdgeLink --draft=false --latest
```

Замените пример версии реальным `VERSION`. Это ручная публикация сопровождающего; пользователи получают релиз через встроенное OTA. GitHub Actions не настроен. Никогда не добавляйте `.edgelink`, секреты Keychain/Credential Manager, токены или личные файлы настроек в Git/релиз.

macOS сборка предпочитает доступный Developer ID / Apple Development сертификат. Можно явно выбрать его через `EDGELINK_SIGN_IDENTITY`; без сертификата используется ad-hoc. Для стабильного доверия Gatekeeper/TCC замените этап подписи на Developer ID, hardened runtime и notarization до упаковки; сохраняйте bundle ID. Иконки генерируются штатным AppKit, исходники находятся в `source/assets/`.

Сохраняйте signing identity между релизами для сохранения TCC. Переход с ad-hoc требует однократного восстановления разрешений через мастер. Apple Development позволяет стабильную идентичность тестовой сборки, но не заменяет Developer ID и notarization для распространения.

On Windows, run `EdgeLink.exe --layout-checks` at the display scales you support. This creates `%TEMP%/EdgeLink-layout-checks/report.json` and PNG snapshots of the native WinUI pages, then exits with code 0 only if the layout checks pass. The check mode skips saved configuration, pairing credentials, input hooks, network and audio. Compile-only validation is not a substitute for this native run.

Before replacing the Windows EXE or running native checks, exit through the tray or window **Меню → Выйти из EdgeLink** and verify that no ordinary EdgeLink process remains. The window close button hides the app while its connection continues. Run `EdgeLink.exe --tray-checks` as well: it writes `%TEMP%/EdgeLink-tray-checks.json` and checks a real Shell notification icon, native close/restore, the second-invocation handler, explicit exit, and the updater/OS-shutdown bypass conditions. The shutdown check never shuts down Windows, and the updater check never installs an update. Verify an actual second process separately. `--ui-preview` provides an isolated interactive preview without settings, pairing, input hooks, network or audio. Verify actual control rendering and keyboard access there, in addition to layout bounds.

On Windows, `EdgeLink.exe --input-checks` tests the dedicated input thread, inactive Raw Input registration and responsiveness during a 300 ms UI stall. It writes `%TEMP%/EdgeLink-input-checks.json`; no pairing, network, audio or synthetic input is used. Native checks must run on Windows. If an installed Mac server must stay connected during cross-language tests, set `EDGELINK_SKIP_DISCOVERY_CHECK=1` to skip the UDP discovery tests only.
