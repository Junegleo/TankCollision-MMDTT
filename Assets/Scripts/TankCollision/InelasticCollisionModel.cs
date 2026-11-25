using System;

namespace TankCollision
{
    /// <summary>
    /// Входные параметры задачи (раздел «Динамика», задача 3).
    /// m1, v1 — снаряд, летит вдоль оси OX.
    /// m2, v2 — танк, едет под углом alpha к оси OX.
    /// </summary>
    [Serializable]
    public class CollisionParams
    {
        public float m1 = 7f;          // масса снаряда, кг
        public float v1 = 800f;        // скорость снаряда, м/с
        public float m2 = 30000f;      // масса танка, кг
        public float v2 = 10f;         // скорость танка, м/с
        public float alphaDeg = 60f;   // угол между V2 и осью OX, градусы

        public CollisionParams Clone() => (CollisionParams)MemberwiseClone();

        public void CopyFrom(CollisionParams o)
        {
            m1 = o.m1; v1 = o.v1; m2 = o.m2; v2 = o.v2; alphaDeg = o.alphaDeg;
        }
    }

    /// <summary>Результат расчёта. Все величины в СИ.</summary>
    public struct CollisionResult
    {
        public double p1;        // импульс снаряда m1*V1
        public double p2;        // импульс танка m2*V2
        public double p2x, p2y;  // проекции импульса танка
        public double px, py;    // проекции импульса системы
        public double p;         // модуль импульса системы после удара
        public double betaDeg;   // угол импульса системы с осью OX
        public double u;         // скорость общего тела после удара
        public double ux, uy;    // её проекции
        public double eShell;    // кинетическая энергия снаряда до удара
        public double eTank;     // кинетическая энергия танка до удара
        public double eBefore;   // суммарная энергия до удара
        public double eAfter;    // энергия после удара
        public double q;         // выделившаяся теплота (по формуле 6)
        public double qFormula;  // теплота по упрощённой формуле 7 (для самопроверки)
        public double qShare;    // доля теплоты от энергии снаряда, 0..1
        public double dvTank;    // изменение модуля скорости танка u - V2
        public double turnDeg;   // на сколько повернулся вектор скорости танка (beta - alpha)
        public double vRel;      // относительная скорость тел |V1 - V2|
    }

    /// <summary>
    /// Математическая модель абсолютно неупругого удара двух частиц под углом.
    /// Закон сохранения импульса:   m1·V1 + m2·V2 = (m1 + m2)·u = p
    /// Теплота:                      Q = E_до − E_после = m1·m2·(V1² + V2² − 2·V1·V2·cosα) / (2·(m1 + m2))
    /// </summary>
    public static class InelasticCollisionModel
    {
        public static CollisionResult Solve(CollisionParams prm)
        {
            double m1 = prm.m1, v1 = prm.v1, m2 = prm.m2, v2 = prm.v2;
            double a = prm.alphaDeg * Math.PI / 180.0;
            double cos = Math.Cos(a), sin = Math.Sin(a);

            var r = new CollisionResult();
            r.p1 = m1 * v1;
            r.p2 = m2 * v2;
            r.p2x = r.p2 * cos;
            r.p2y = r.p2 * sin;

            // (2), (3): проекции импульса системы
            r.px = r.p1 + r.p2x;
            r.py = r.p2y;

            // (4): модуль, (5): направление
            r.p = Math.Sqrt(r.px * r.px + r.py * r.py);
            r.betaDeg = Math.Atan2(r.py, r.px) * 180.0 / Math.PI;

            // скорость общего тела
            double M = m1 + m2;
            r.u = r.p / M;
            r.ux = r.px / M;
            r.uy = r.py / M;

            // (6): теплота как убыль кинетической энергии
            r.eShell = m1 * v1 * v1 / 2.0;
            r.eTank = m2 * v2 * v2 / 2.0;
            r.eBefore = r.eShell + r.eTank;
            r.eAfter = r.p * r.p / (2.0 * M);
            r.q = r.eBefore - r.eAfter;

            // (7): упрощённая формула
            double vRel2 = v1 * v1 + v2 * v2 - 2.0 * v1 * v2 * cos;
            r.vRel = Math.Sqrt(Math.Max(0.0, vRel2));
            r.qFormula = m1 * m2 * vRel2 / (2.0 * M);

            r.qShare = r.eShell > 0 ? r.q / r.eShell : 0;
            r.dvTank = r.u - v2;
            r.turnDeg = r.betaDeg - prm.alphaDeg;
            return r;
        }
    }
}
