# Правила для агента (читать целиком)

Читай только: этот файл, `Docs/GDD.md`, свою карточку в `Docs/CARDS.md`, и код в `Assets/_Game/` по мере необходимости (точечно). Не делай обзор всего проекта, не читай `Library/`, `Temp/`, `Packages/` (кроме `manifest.json`).

## Архитектура (не менять без запроса)
- Весь код: `Assets/_Game/Scripts/` (runtime, namespace `Friendslop`) и `Assets/_Game/Editor/` (namespace `Friendslop.EditorTools`).
- **Сцены и префабы руками не правим.** Их создаёт `Friendslop.EditorTools.Builder.BuildAll()` (меню `Tools/Friendslop/Build All`) из примитивов: префабы → `Assets/_Game/Prefabs/`, сцены `Menu` и `Motel` → `Assets/_Game/Scenes/`, регистрирует network-префабы и сцены в Build Settings, запекает NavMesh. Каждая карточка дописывает свою часть в Builder.
- Сеть: NGO 2.x, сервер-авторитет (хост). Ввод игрока — `[Rpc(SendTo.Server)]`, сервер проверяет дистанцию. Движение игрока — owner-authority NetworkTransform. НПС и логика — только на сервере.
- Предмет в руках = `NetworkVariable<ItemType>` у игрока + визуал в руке. Бросок/положить — сервер спавнит объект предмета. Никакого парентинга NetworkObject.
- UI — IMGUI (`OnGUI`), без Canvas/TMP. Строки — по-русски прямо в коде.
- Числа баланса — только в `GameConfig` (ScriptableObject, `Assets/_Game/Config/GameConfig.asset`).
- Модели: пока примитивы с цветными материалами. Ввод: Input System, клава+мышь.

## Проверка (обязательна перед коммитом)
`powershell -ExecutionPolicy Bypass -File tools/check.ps1` (аналогично build.ps1) — запускает Unity в batchmode, выполняет BuildAll, ищет ошибки. Нужен результат `CHECK OK`. Ошибка → читай только строки ошибок из `Logs/check.log`.
Unity-редактор при этом должен быть закрыт (оркестратор закрывает его сам).

## Git
Один коммит на карточку: `[C<N>] кратко`. Не пушить. Новые пакеты — только указанные в карточке.

## В конце карточки
1. Допиши в `Docs/DECISIONS.md` 2–4 строки: `- **Решение** — альтернатива — почему` (для лекции по геймдизайну: в первую очередь решения о геймплее и прототипировании, не о синтаксисе).
2. Ответ оркестратору ≤10 строк: DONE/PARTIAL/BLOCKED, что сделано, что проверить руками в плейтесте, проблемы. Код не пересказывай.

