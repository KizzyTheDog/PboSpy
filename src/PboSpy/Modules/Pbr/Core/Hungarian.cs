namespace PboSpy.Modules.Pbr.Core;

/// <summary>Kuhn–Munkres (Jonker-style potentials), O(n²m).</summary>
public static class Hungarian
{
    /// <summary>
    /// Minimum cost assignment for an n×m matrix with n ≤ m. Returns, for each row, the column it gets.
    /// </summary>
    public static int[] Solve(double[,] cost)
    {
        var n = cost.GetLength(0);
        var m = cost.GetLength(1);
        if (n > m)
        {
            throw new ArgumentException("Hungarian.Solve needs rows <= columns.");
        }
        var u = new double[n + 1];
        var v = new double[m + 1];
        var p = new int[m + 1];
        var way = new int[m + 1];
        for (var i = 1; i <= n; i++)
        {
            p[0] = i;
            var j0 = 0;
            var minv = new double[m + 1];
            var used = new bool[m + 1];
            Array.Fill(minv, double.PositiveInfinity);
            do
            {
                used[j0] = true;
                var i0 = p[j0];
                var delta = double.PositiveInfinity;
                var j1 = 0;
                for (var j = 1; j <= m; j++)
                {
                    if (used[j])
                    {
                        continue;
                    }
                    var current = cost[i0 - 1, j - 1] - u[i0] - v[j];
                    if (current < minv[j])
                    {
                        minv[j] = current;
                        way[j] = j0;
                    }
                    if (minv[j] < delta)
                    {
                        delta = minv[j];
                        j1 = j;
                    }
                }
                for (var j = 0; j <= m; j++)
                {
                    if (used[j])
                    {
                        u[p[j]] += delta;
                        v[j] -= delta;
                    }
                    else
                    {
                        minv[j] -= delta;
                    }
                }
                j0 = j1;
            }
            while (p[j0] != 0);
            do
            {
                var j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            }
            while (j0 != 0);
        }
        var result = new int[n];
        for (var j = 1; j <= m; j++)
        {
            if (p[j] != 0)
            {
                result[p[j] - 1] = j - 1;
            }
        }
        return result;
    }
}
