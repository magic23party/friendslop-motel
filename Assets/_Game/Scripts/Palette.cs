using UnityEngine;

namespace Friendslop
{
    /// <summary>
    /// Общая палитра арт-дирекшна (P1): ночной придорожный мотель, плоские цвета.
    /// Бирюза, тёмно-красный, кремовый, тёмное дерево, тёплый жёлтый свет; холодная синяя ночь снаружи.
    /// Цвета заданы как sRGB (как в инспекторе); Builder кладёт их в URP Lit без текстур.
    /// </summary>
    public static class Palette
    {
        // основа
        public static readonly Color Wall = new Color(0.90f, 0.85f, 0.71f);       // кремовая стена
        public static readonly Color Cream = new Color(0.94f, 0.90f, 0.78f);
        public static readonly Color Ceiling = new Color(0.88f, 0.84f, 0.74f);
        public static readonly Color Roof = new Color(0.16f, 0.17f, 0.20f);
        public static readonly Color Floor = new Color(0.80f, 0.74f, 0.60f);      // светлая плитка
        public static readonly Color FloorTile = new Color(0.15f, 0.52f, 0.53f);  // тёмная бирюзовая плитка
        public static readonly Color Carpet = new Color(0.42f, 0.10f, 0.14f);     // ковёр коридора
        public static readonly Color RoomCarpet = new Color(0.12f, 0.31f, 0.34f); // ковролин номеров

        // акценты
        public static readonly Color Teal = new Color(0.14f, 0.64f, 0.63f);
        public static readonly Color TealDark = new Color(0.08f, 0.34f, 0.37f);
        public static readonly Color TealLight = new Color(0.40f, 0.80f, 0.76f);
        public static readonly Color DarkRed = new Color(0.46f, 0.10f, 0.13f);
        public static readonly Color Red = new Color(0.78f, 0.16f, 0.18f);
        public static readonly Color Mustard = new Color(0.92f, 0.70f, 0.16f);

        // дерево, металл, картон
        public static readonly Color WoodDark = new Color(0.20f, 0.12f, 0.08f);
        public static readonly Color Wood = new Color(0.38f, 0.23f, 0.13f);
        public static readonly Color WoodLight = new Color(0.60f, 0.42f, 0.24f);
        public static readonly Color Metal = new Color(0.42f, 0.45f, 0.50f);
        public static readonly Color MetalDark = new Color(0.18f, 0.19f, 0.22f);
        public static readonly Color Cardboard = new Color(0.66f, 0.49f, 0.30f);

        // растения, улица
        public static readonly Color Green = new Color(0.14f, 0.46f, 0.25f);
        public static readonly Color GreenLight = new Color(0.30f, 0.62f, 0.30f);
        public static readonly Color Terracotta = new Color(0.62f, 0.30f, 0.18f);
        public static readonly Color Asphalt = new Color(0.14f, 0.15f, 0.18f);
        public static readonly Color Concrete = new Color(0.30f, 0.31f, 0.33f);
        public static readonly Color Grass = new Color(0.05f, 0.12f, 0.09f);

        // свет
        public static readonly Color LampWarm = new Color(1.00f, 0.80f, 0.52f);   // лампы внутри
        public static readonly Color Moon = new Color(0.50f, 0.60f, 1.00f);       // холодный лунный
        public static readonly Color Night = new Color(0.07f, 0.11f, 0.25f);     // фон неба
        public static readonly Color Ambient = new Color(0.33f, 0.33f, 0.43f);
        public static readonly Color Neon = new Color(1.00f, 0.18f, 0.34f);

        // цвета предметов на стеллажах (стеллаж читается цветом своего предмета)
        public static readonly Color Towel = new Color(0.35f, 0.65f, 0.95f);
        public static readonly Color Soap = new Color(0.95f, 0.55f, 0.75f);
        public static readonly Color Napkins = new Color(0.98f, 0.95f, 0.70f);
        public static readonly Color Mop = new Color(0.35f, 0.72f, 0.35f);
        public static readonly Color Hammer = new Color(0.80f, 0.20f, 0.20f);
        public static readonly Color Dirt = new Color(0.60f, 0.38f, 0.16f);        // значок «грязь» на табличке номера
    }
}
