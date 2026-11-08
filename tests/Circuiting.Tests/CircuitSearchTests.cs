using Circuiting.Models;
using Circuiting.Services;
using Circuiting.ViewModels;
using Xunit;

namespace Circuiting.Tests;

public class CircuitSearchTests
{
    private static (List<BoardData> Boards, CircuitData Lights, CircuitData Supply) Project()
    {
        var kitchen = CircuitLayoutTests.Chassis(12);
        kitchen.Name = "Kitchen";
        var lights = new CircuitData
        {
            CircuitGuid = Guid.NewGuid(), PoleIndex = 0, Description = "Lights",
            BreakerSize = "16A", ProtectionType = "RCBO", EarthLeakage = "30mA"
        };
        var supply = new CircuitData
        {
            CircuitGuid = Guid.NewGuid(), PoleIndex = 1, Poles = 3, Description = "Pump supply",
            BreakerSize = "32A", ProtectionType = "MCB"
        };
        kitchen.Circuits.AddRange(new[] { lights, supply });
        var first = CircuitLayoutTests.Board(kitchen);
        first.DisplayName = "DB-GF";
        CircuitLayout.ValidateAndUpdate(first, kitchen);

        var plant = CircuitLayoutTests.Chassis(12);
        plant.Name = "Plant";
        var second = CircuitLayoutTests.Board(plant);
        second.DisplayName = "DB-Lights";
        return (new List<BoardData> { first, second }, lights, supply);
    }

    private static List<BoardTreeNode> Build(List<BoardData> boards, string text, CircuitSearchScope scope,
        out int matches, ISet<Guid>? expanded = null) =>
        CircuitTreeProjection.Build(boards, new CircuitTreeProjectionTests.RecordingActions(),
            expanded ?? new HashSet<Guid>(), new CircuitSearch(text, scope), out matches);

    [Fact]
    public void EmptySearchShowsTheWholeTreeWithUserExpansion()
    {
        var (boards, _, _) = Project();

        var nodes = Build(boards, "   ", CircuitSearchScope.Circuits, out int matches,
            new HashSet<Guid> { boards[1].BoardGuid });

        Assert.Equal(0, matches);
        Assert.Equal(2, nodes.Count);
        Assert.False(nodes[0].IsExpanded);
        Assert.True(nodes[1].IsExpanded);
        Assert.Equal(6, nodes[0].Chassis[0].Rows.Count);
        Assert.DoesNotContain(nodes, n => n.IsSearchMatch);
    }

    [Fact]
    public void BoardScopeFindsOnlyDbsAndKeepsTheirContents()
    {
        var (boards, _, _) = Project();

        var node = Assert.Single(Build(boards, "lights", CircuitSearchScope.Boards, out int matches));

        Assert.Equal(1, matches);
        Assert.Same(boards[1], node.Board);
        Assert.True(node.IsSearchMatch);
        Assert.True(node.IsExpanded);
        Assert.Equal(6, Assert.Single(node.Chassis).Rows.Count);
    }

    [Fact]
    public void CircuitScopeShowsOnlyMatchingRowsUnderExpandedAncestors()
    {
        var (boards, lights, _) = Project();

        var node = Assert.Single(Build(boards, "lights", CircuitSearchScope.Circuits, out int matches));

        Assert.Equal(1, matches);
        Assert.Same(boards[0], node.Board);
        Assert.False(node.IsSearchMatch);
        Assert.True(node.IsExpanded);
        var chassis = Assert.Single(node.Chassis);
        Assert.True(chassis.IsExpanded);
        var row = Assert.Single(chassis.Rows);
        Assert.Same(lights, row.LeftCircuit);
        Assert.True(row.IsSearchMatch);
    }

    [Fact]
    public void AllScopeCombinesBoardAndCircuitMatches()
    {
        var (boards, _, _) = Project();

        var nodes = Build(boards, "LIGHTS", CircuitSearchScope.All, out int matches);

        Assert.Equal(2, matches);
        Assert.Equal(new[] { boards[0], boards[1] }, nodes.Select(n => n.Board));
        Assert.Single(nodes[0].Chassis[0].Rows);
        Assert.Equal(6, nodes[1].Chassis[0].Rows.Count);
    }

    [Fact]
    public void ChassisScopeMatchesChassisNameOnly()
    {
        var (boards, _, _) = Project();

        var node = Assert.Single(Build(boards, "plant", CircuitSearchScope.Chassis, out int matches));
        Assert.Equal(1, matches);
        var chassis = Assert.Single(node.Chassis);
        Assert.True(chassis.IsSearchMatch);
        Assert.Equal(6, chassis.Rows.Count);

        Assert.Empty(Build(boards, "lights", CircuitSearchScope.Chassis, out matches));
        Assert.Equal(0, matches);
    }

    [Fact]
    public void EveryTermMustMatchACircuitFieldAndMultiPoleCircuitsShowAllPoles()
    {
        var (boards, lights, supply) = Project();

        var rows = Assert.Single(Build(boards, "16A rcbo", CircuitSearchScope.Circuits, out int matches))
            .Chassis[0].Rows;
        Assert.Equal(1, matches);
        Assert.Same(lights, Assert.Single(rows).LeftCircuit);
        Assert.Empty(Build(boards, "16A MCB", CircuitSearchScope.Circuits, out _));

        rows = Assert.Single(Build(boards, "pump", CircuitSearchScope.Circuits, out matches)).Chassis[0].Rows;
        Assert.Equal(1, matches);
        Assert.Equal(3, rows.Count);
        Assert.Same(supply, rows[0].RightCircuit);
        Assert.Same(supply, rows[2].RightConsumedInfo?.Parent);
    }

    [Fact]
    public void FilteredRowsKeepTheirCommands()
    {
        var (boards, lights, _) = Project();
        var actions = new CircuitTreeProjectionTests.RecordingActions();

        var row = CircuitTreeProjection.Build(boards, actions, new HashSet<Guid>(),
            new CircuitSearch(lights.DisplayLabel + " lights", CircuitSearchScope.Circuits), out _)
            .Single().Chassis[0].Rows.Single();
        row.LeftCommands!.MoveCommand!.Execute(null);

        Assert.Equal(("move-circuit", (object)lights), (actions.Action, actions.Target));
    }
}
