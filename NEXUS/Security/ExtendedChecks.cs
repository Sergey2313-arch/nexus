namespace NEXUS.Security;

internal static class ExtendedChecks
{
    internal const string Processes = """
        $processes = @(Get-CimInstance Win32_Process -ErrorAction Stop)
        foreach ($p in $processes) {
            InspectFile $p.ExecutablePath 'Processes'
            $parent = $processes | Where-Object ProcessId -eq $p.ParentProcessId | Select-Object -First 1
            if ($parent.Name -ne 'NEXUS.exe' -and $p.CommandLine -match '(?i)-(enc|encodedcommand)\s' -and $p.Name -match '(?i)powershell|pwsh') {
                [pscustomobject]@{Severity='Warning';Category='Processes';Title='PowerShell с кодированной командой';Evidence=('PID=' + $p.ProcessId + '; ParentPID=' + $p.ParentProcessId + '; ' + $p.CommandLine);Recommendation='Проверьте родительский процесс и назначение команды. Кодирование используется и легитимными приложениями, включая NEXUS.'}
            }
        }
        """;
    internal const string Startup = """
        Get-CimInstance Win32_StartupCommand -ErrorAction Stop | ForEach-Object { InspectFile $_.Command 'Startup' }
        foreach ($root in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Run','HKLM:\Software\Microsoft\Windows\CurrentVersion\Run','HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce','HKLM:\Software\Microsoft\Windows\CurrentVersion\RunOnce','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run')) {
            if (Test-Path $root) {
                (Get-ItemProperty $root -ErrorAction Stop).PSObject.Properties | Where-Object Name -notmatch '^PS' | ForEach-Object { InspectFile ([string]$_.Value) 'Startup' }
            }
        }
        Get-ScheduledTask -ErrorAction Stop | Where-Object State -ne 'Disabled' | ForEach-Object {
            foreach ($action in $_.Actions) { InspectFile $action.Execute 'Tasks' }
        }
        """;
    internal const string Files = """
        $roots = @($env:TEMP, [Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('MyDocuments'), (Join-Path $env:USERPROFILE 'Downloads')) | Select-Object -Unique
        $total = 0
        foreach ($root in $roots) {
            if (-not (Test-Path -LiteralPath $root)) { continue }
            $queue = [Collections.Generic.Queue[string]]::new(); $queue.Enqueue($root)
            $visited = 0
            while ($queue.Count -gt 0 -and $visited -lt 500 -and $total -lt 1000) {
                $dir = $queue.Dequeue(); $visited++
                foreach ($item in Get-ChildItem -LiteralPath $dir -Force -ErrorAction Stop) {
                    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
                    if ($item.PSIsContainer) { $queue.Enqueue($item.FullName) }
                    elseif ($item.Extension -match '(?i)^\.(exe|dll|sys|ps1|vbs|js|bat|cmd)$') {
                        $total++; InspectFile $item.FullName 'Files'
                        if ($total -ge 1000) { break }
                    }
                }
            }
        }
        [pscustomobject]@{Severity='Info';Category='Coverage';Title='Объём проверки файлов';Evidence=('Проверено кандидатов: ' + $total + '; максимум 1000 файлов и 500 каталогов на корень; точки повторного анализа пропущены.');Recommendation='При необходимости выполните полную проверку дисков в Defender.'}
        """;
    internal const string Network = """
        Get-NetTCPConnection -State Established -ErrorAction Stop | ForEach-Object {
            $p = Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue
            if ($p) { InspectFile $p.Path 'Network' }
            [pscustomobject]@{Severity='Info';Category='Network';Title='Установленное TCP-соединение';Evidence=('PID=' + $_.OwningProcess + '; Process=' + $p.ProcessName + '; ' + $_.LocalAddress + ':' + $_.LocalPort + ' -> ' + $_.RemoteAddress + ':' + $_.RemotePort);Recommendation='Сравните соединение с назначением приложения. Адрес сам по себе не доказывает угрозу.'}
        }
        """;
    internal const string Configuration = """
        Get-NetFirewallProfile -ErrorAction Stop | Where-Object { -not $_.Enabled } | ForEach-Object {
            [pscustomobject]@{Severity='Warning';Category='Configuration';Title='Профиль брандмауэра отключён';Evidence=$_.Name;Recommendation='Проверьте настройки брандмауэра и наличие альтернативной защиты.'}
        }
        $uac = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -ErrorAction Stop
        if ($uac.EnableLUA -eq 0) {
            [pscustomobject]@{Severity='Warning';Category='Configuration';Title='UAC отключён';Evidence='EnableLUA=0';Recommendation='Включите контроль учётных записей Windows.'}
        }
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Для SFC /verifyonly и журнала Security запустите NEXUS от администратора.' }
        $sfc = & "$env:windir\System32\sfc.exe" /verifyonly 2>&1 | Out-String
        $code = $LASTEXITCODE
        [pscustomobject]@{Severity='Info';Category='Integrity';Title='SFC /verifyonly: проверка целостности';Evidence=('ExitCode=' + $code + '; ' + $sfc.Trim());Recommendation='Оцените текст результата SFC. При найденных нарушениях сделайте резервную копию и используйте SFC/DISM вручную; NEXUS ничего не исправляет автоматически.'}
        $dism = & "$env:windir\System32\dism.exe" /Online /Cleanup-Image /ScanHealth /English 2>&1 | Out-String
        $dismCode = $LASTEXITCODE
        [pscustomobject]@{Severity='Info';Category='Integrity';Title='DISM /ScanHealth: хранилище компонентов';Evidence=('ExitCode=' + $dismCode + '; ' + $dism.Trim());Recommendation='Оцените результат DISM. Восстановление /RestoreHealth автоматически не выполняется.'}
        if ($dismCode -ne 0) { throw ('DISM завершился с кодом ' + $dismCode) }
        if ($dism -match '(?i)component store is repairable|component store cannot be repaired') {
            [pscustomobject]@{Severity='Warning';Category='Integrity';Title='DISM сообщил о повреждении хранилища компонентов';Evidence=$dism.Trim();Recommendation='Сделайте резервную копию и проверьте возможность восстановления средствами DISM. Ремонт не выполнялся.'}
        }
        if ($code -ne 0) { throw ('SFC завершился с кодом ' + $code + '; оцените сохранённый вывод') }
        $events = @(Get-WinEvent -FilterHashtable @{LogName='Security';Id=4625,1102;StartTime=(Get-Date).AddDays(-7)} -MaxEvents 100 -ErrorAction SilentlyContinue -ErrorVariable auditError)
        if ($auditError -and $auditError[0].FullyQualifiedErrorId -notmatch 'NoMatchingEventsFound') { throw $auditError[0] }
        foreach ($event in $events | Where-Object Id -eq 1102) {
            [pscustomobject]@{Severity='Warning';Category='Events';Title='Журнал аудита Security был очищен';Evidence=($event.TimeCreated.ToString('o') + '; EventID=1102; ' + $event.Message);Recommendation='Уточните, было ли это плановое действие администратора. Событие не доказывает взлом.'}
        }
        if ($events.Where({$_.Id -eq 4625}).Count -gt 0) {
            [pscustomobject]@{Severity='Info';Category='Events';Title='Неудачные входы за последние 7 дней';Evidence=('В выборке до 100 событий: ' + $events.Where({$_.Id -eq 4625}).Count);Recommendation='Проверьте журнал Security, время и источник входов. Ошибки пароля возможны без атаки.'}
        }
        """;
}
