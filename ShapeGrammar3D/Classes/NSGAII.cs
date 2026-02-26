using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection.Metadata;

Console.WriteLine("Hello, World!");

const int PopSize = 100;
const int Generations = 200;
const int VariableCount = 3;
var rand = new Random(43);

// initial population

var population = Enumerable.Range(0, PopSize)
    .Select(_ => Enumerable.Range(0, VariableCount).Select(_ => rand.NextDouble()).ToArray())
    .ToList();
// Console.WriteLine($"Initial population: {string.Join(", ", population)}");

for (int gen = 0; gen < Generations; gen++)
{
    // generate offspring
    var offspring = new List<double[]>();
    while (offspring.Count < PopSize)
    {
        var p1 = TournamentSelect(population, rand);
        var p2 = TournamentSelect(population, rand);
        var (c1, c2) = Crossover(p1, p2, rand);
        Mutate(c1, rand);
        Mutate(c2, rand);
        offspring.Add(c1);
        offspring.Add(c2);
    }

    var combined = population.Concat(offspring).ToList();
    population = SelectNextGeneration(combined, PopSize);

    var f = NonDominatedSort(population);
    Console.WriteLine($"gen {gen}: front count ={f.Count}, first front ={f[0].Count} individuals");

}

// show pareto front
var fronts = NonDominatedSort(population);
Console.WriteLine("=== pareto solutions (f1, f2) ===");
foreach (var ind in fronts[0].OrderBy(x => Evaluate(x).f1))
{
    var (f1, f2) = Evaluate(ind);
    Console.WriteLine($"f1={f1:F4}, f2={f2:F4}");
}


// --- objective functions ---
// example: f1 = x0, f2 = g * (1 - sqrt(x0/g)), g = 1 + 9*Σxi/(n-1)
static (double f1, double f2) Evaluate(double[] x)
{
    double g = 1.0 + 9.0 * x[1..].Sum() / (x.Length - 1);
    double f1 = x[0];
    double f2 = g * (1.0 - Math.Sqrt(f1 / g));

    return (f1, f2);
}

// dominates
static bool Dominates(double[] a, double[] b)
{
    var (a1, a2) = Evaluate(a);
    var (b1, b2) = Evaluate(b);
    bool betterInAny = a1 < b1 || a2 < b2;
    bool worseInNone = a1 <= b1 && a2 <= b2;

    return worseInNone && betterInAny;
}

// non dominated sort
static List<List<double[]>> NonDominatedSort(List<double[]> pop)
{
    int n = pop.Count;
    var dominationCount = new int[n];
    var dominated = new List<int>[n];
    for (int i = 0; i < n; i++)
    {
        dominated[i] = new List<int>();
    }

    var fronts = new List<List<double[]>>();
    var firstFront = new List<int>();

    for (int i = 0; i < n; i++)
    {
        for (int j = i + 1; j < n; j++)
        {
            if (Dominates(pop[i], pop[j]))
            {
                dominated[i].Add(j);
                dominationCount[j]++;
            }

            else if (Dominates(pop[j], pop[i]))
            {
                dominated[j].Add(i);
                dominationCount[i]++;
            }
        }
        if (dominationCount[i] == 0)
        {
            firstFront.Add(i);
        }
    }

    var currentIndices = firstFront;
    while (currentIndices.Count > 0)
    {
        fronts.Add(currentIndices.Select(i => pop[i]).ToList());
        var nextIndices = new List<int>();

        foreach (int i in currentIndices)
        {
            foreach (int j in dominated[i])
            {
                dominationCount[j]--;
                if (dominationCount[j] == 0)
                {
                    nextIndices.Add(j);
                }
            }
        }
        currentIndices = nextIndices;
    }

    return fronts;
}

// crowdness distance
static double[] CrowdingDistance(List<double[]> front)
{
    int n = front.Count;
    var dist = new double[n];
    if (n <= 2)
    {
        Array.Fill(dist, double.MaxValue);
        return dist;
    }

    // foreach objective function, sort and add distance
    foreach (var selector in new Func<double[], double>[] { x => Evaluate(x).f1, x => Evaluate(x).f2 })
    {
        var idx = Enumerable.Range(0, n).OrderBy(i => selector(front[i])).ToArray();
        dist[idx[0]] = dist[idx[^1]] = double.MaxValue;
        double range = selector(front[idx[^1]]) - selector(front[idx[0]]);
        if (range == 0)
        {
            continue;
        }

        for (int i = 1; i < n - 1; i++)
        {
            dist[idx[i]] += (selector(front[idx[i + 1]]) - selector(front[idx[i - 1]])) / range;
        }
    }

    return dist;
}

// select next gen
static List<double[]> SelectNextGeneration(List<double[]> combined, int size)
{
    var fronts = NonDominatedSort(combined);
    var next = new List<double[]>();

    foreach (var front in fronts)
    {
        if (next.Count + front.Count <= size)
        {
            next.AddRange(front);
        }

        else
        {
            // select according to the crowidng distance (big to small)
            var cd = CrowdingDistance(front);
            var sorted = Enumerable.Range(0, front.Count)
                .OrderByDescending(i => cd[i])
                .Take(size - next.Count);
            next.AddRange(sorted.Select(i => front[i]));
            break;
        }
    }
    return next;
}

// tournament selection
static double[] TournamentSelect(List<double[]> pop, Random rand)
{
    var a = pop[rand.Next(pop.Count)];
    var b = pop[rand.Next(pop.Count)];
    if (Dominates(a, b)) return a;
    if (Dominates(b, a)) return b;

    return rand.NextDouble() < 0.5 ? a : b;

}

// sbx crossover (simulated binary crossover)
static (double[], double[]) Crossover(double[] p1, double[] p2, Random rand)
{
    var c1 = (double[])p1.Clone();
    var c2 = (double[])p2.Clone();
    if (rand.NextDouble() < 0.9) // crossover rate
    {
        for (int i = 0; i < c1.Length; i++)
        {
            double alpha = rand.NextDouble();
            c1[i] = alpha * p1[i] + (1 - alpha) * p2[i];
            c2[i] = (1 - alpha) * p1[i] + alpha * p2[i];
        }
    }
    return (c1, c2);
}

// mutation
static void Mutate(double[] x, Random rand, double rate = 0.1)
{
    for (int i = 0; i < x.Length; i++)
    {
        if (rand.NextDouble() < rate)
        {
            x[i] = Math.Clamp(x[i] + (rand.NextDouble() - 0.5) * 0.2, 0.0, 1.0);
        }
    }
}