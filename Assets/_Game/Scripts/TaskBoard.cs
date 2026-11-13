using System.Collections.Generic;

namespace Friendslop
{
    public struct TaskLine
    {
        public string text;
        public bool urgent;       // красным и выше в списке
        public ItemType[] icons;  // (P3) что нужно: цветные значки предметов справа от текста (null — без значков)
        public TaskLine(string text, bool urgent = false, ItemType[] icons = null) { this.text = text; this.urgent = urgent; this.icons = icons; }
    }

    /// <summary>
    /// Общий список задач отеля (справа в HUD). Встроенные: неоплаченный счёт и номера, требующие обслуживания.
    /// Остальные системы (гость у стойки — C4, драка — C5) добавляют одной строкой:
    ///   TaskBoard.Register(into => { if (cond) into.Add(new TaskLine("Гость у стойки")); });
    /// (сохраните делегат в поле и снимите в OnNetworkDespawn: TaskBoard.Unregister(тот же делегат)).
    /// Провайдеры вызываются на каждой машине: читайте только NetworkVariable / синхронное состояние.
    /// </summary>
    public static class TaskBoard
    {
        public delegate void Provider(List<TaskLine> into);

        static readonly List<Provider> providers = new List<Provider>();

        public static void Register(Provider p) { if (p != null && !providers.Contains(p)) providers.Add(p); }
        public static void Unregister(Provider p) { providers.Remove(p); }

        /// <summary>Собрать актуальный список: счёт, провайдеры, номера. Список очищается. Вне смены — пусто.</summary>
        public static void Collect(List<TaskLine> into)
        {
            into.Clear();
            var gs = GameState.Instance;
            if (gs == null || gs.Phase.Value != GamePhase.Playing) return;

            if (gs.TaxDue.Value)
                into.Add(new TaskLine($"Счёт {gs.TaxAmount} $ не оплачен — терминал налогов ({Fmt(gs.TaxLeft)})", true));
            else if (gs.NightSoon)
                into.Add(new TaskLine($"Ночь через {Fmt(gs.PhaseLeft)} — приготовь {gs.TaxAmount} $ на налог", gs.Money.Value < gs.TaxAmount));

            foreach (var p in providers) p(into);

            foreach (var r in Room.All)
            {
                if (r.Locked) continue;
                if (r.State == RoomState.NeedsService)
                {
                    string plumber = r.HasClog && r.PlumberPending ? $" · сантехник {Fmt(r.PlumberLeft)}" : "";
                    into.Add(new TaskLine($"Номер {r.index}{plumber}", false, r.NeedIcons()));
                }
                else if (r.LampBroken && r.State == RoomState.Ready)       // V2: лампа — номер можно сдать, но с потерей в оплате
                    into.Add(new TaskLine($"Номер {r.index}: лампа", false, new[] { ItemType.Bulb }));
            }
        }

        public static string Fmt(float seconds)
        {
            int s = UnityEngine.Mathf.CeilToInt(seconds);
            return (s / 60) + ":" + (s % 60).ToString("00");
        }
    }
}
