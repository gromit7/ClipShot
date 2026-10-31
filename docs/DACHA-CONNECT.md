# Первое подключение Codex Дача

Передать этот текст дачному Codex. Это однократная установка клиента, не просьба настроить новую сеть или скопировать ключ с работы.

Владелец поручил организовать совместную работу: Codex Работа главный, DeepSeek второй специалист, Codex Дача третий специалист, подключающийся по необходимости. Уже развёрнут частный SSH MCP codex-link на личном Gateway vpn.data-clouds.ru. Рабочая сторона и DeepSeek проверили обмен; Дача ещё не подтверждена.

1. Использовать собственный существующий разрешённый SSH-доступ к root@vpn.data-clouds.ru с обязательной проверкой host key. Если доступа нет — сообщить точную ошибку. Не менять VPN/firewall/SSH и не копировать чужой приватный ключ.
2. Скачать /opt/codex-link/v1/bootstrap.ps1 через SCP и запустить. Он установит роль dacha, проверит настоящий вызов MCP, добавит только секцию codex-link в клиентскую конфигурацию с резервной копией и отправит HELLO.
3. Открыть inbox через новый MCP или CLI. Конфигурация и клиент находятся в .codex/tools/codex-link профиля пользователя. CLI client.py --config config.json poll работает без перезапуска чата.
4. Принять задание seq3 / CS-DACHA-UPDATE-20261031. Выполнить только read-only диагностику: версии EXE/DLL/Updater, полные SHA, ОС/build, доступность RF/DE feed и стадия отказа. Не читать пользовательские базы/буфер; не нажимать ручную проверку, способную сразу установить обновление.
5. Ответить result адресату work, thread clipshot/update-dacha-20261031, reply_to=3; отдельный вопрос можно адресовать deepseek. WORK автоматически обработает обращение к DeepSeek и вернёт его независимый ответ через тот же канал.
6. Пока идёт эта диагностика, самостоятельно создать heartbeat своего чата на чтение codex-link раз в5мин, с тишиной при пустом/неизменном inbox. Использовать dedup, явные received/done и evidence. После принятия результата остановить монитор; канал сохранить. Задание владельца о совместной диагностике разрешает адресованные WORK сообщения и этот ограниченный опрос.

Команда первого подключения (одна физическая строка PowerShell):

~~~powershell
$p=Join-Path $env:TEMP "codex-link-bootstrap.ps1"; $k=Join-Path $env:USERPROFILE ".ssh/maxbot_codex_ed25519"; if(Test-Path -LiteralPath $k){scp -o BatchMode=yes -o StrictHostKeyChecking=yes -o ConnectTimeout=10 -i $k "root@vpn.data-clouds.ru:/opt/codex-link/v1/bootstrap.ps1" $p}else{scp -o BatchMode=yes -o StrictHostKeyChecking=yes -o ConnectTimeout=10 "root@vpn.data-clouds.ru:/opt/codex-link/v1/bootstrap.ps1" $p}; if($LASTEXITCODE -eq 0){powershell -NoProfile -ExecutionPolicy Bypass -File $p}
~~~

Подлинный ключ хоста должен уже быть известен дачному ПК. Если проверка SSH не прошла, установщик не изменяет безопасность и не обходит отказ. Python3.11+ берётся из штатного runtime Codex, дополнительных Python-пакетов не требуется.
