using System.Diagnostics;
using System.Collections.Immutable;
using System.Text.Json;
using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Core;
using GestionProduccion.Shared.ModelingGrading;
using Xunit.Abstractions;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingOperationalMeasurementTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("rectangle", 1, 1)]
    [InlineData("rectangle", 8, 5)]
    [InlineData("rectangle", 32, 9)]
    [InlineData("ordinary-curve", 1, 4)]
    [InlineData("ordinary-curve", 8, 5)]
    [InlineData("ordinary-curve", 32, 9)]
    [InlineData("strong-curve", 1, 1)]
    [InlineData("near-linear", 1, 1)]
    public void RecordBoundedSynchronousEngineAndConversionMeasurements(string shape, int pieces, int sizes)
    {
        var document = GradingOperationalFixtures.Create(shape, pieces, sizes);
        var sourceJson = JsonSerializer.Serialize(document);
        var baseline = Execute(document);
        AssertValid(baseline, pieces * sizes);
        var baselineJson = JsonSerializer.Serialize(baseline);
        var artifacts = ArtifactMeasurements(document);
        var proofProbe = ProbeProofWork(document);
        for (var repeat = 1; repeat <= 3; repeat++)
        {
            var measured = Measure(document);
            AssertValid(measured.Result, pieces * sizes);
            Assert.Equal(baselineJson, JsonSerializer.Serialize(measured.Result));
            Assert.Equal(sourceJson, JsonSerializer.Serialize(document));
            WriteMeasurement(document, shape, repeat, measured, artifacts, proofProbe);
        }
    }

    private static Measurement Measure(InternalGeometryDocument document)
    {
        // Serialization, assertions and output are outside the measured synchronous regions.
        var engine = new GradingEngine();
        var converter = new GradingPolygonizer(new(24, 20000));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        var calculation = engine.Calculate(document, new(24, 20000));
        var engineEnd = Stopwatch.GetTimestamp();
        var afterEngine = GC.GetAllocatedBytesForCurrentThread();
        var polygons = calculation.Geometry.Select(g => converter.Convert(g.Contour)).ToArray();
        var end = Stopwatch.GetTimestamp();
        var afterConversion = GC.GetAllocatedBytesForCurrentThread();
        return new(new(calculation, polygons), Stopwatch.GetElapsedTime(start, engineEnd).TotalMilliseconds,
            Stopwatch.GetElapsedTime(engineEnd, end).TotalMilliseconds,
            afterEngine - before, afterConversion - afterEngine);
    }

    private static Result Execute(InternalGeometryDocument document)
    {
        var calculation = new GradingEngine().Calculate(document, new(24, 20000));
        var converter = new GradingPolygonizer(new(24, 20000));
        return new(calculation, calculation.Geometry.Select(g => converter.Convert(g.Contour)).ToArray());
    }

    private static void AssertValid(Result result, int geometryCount)
    {
        Assert.True(result.Calculation.IsApprovable, JsonSerializer.Serialize(result.Calculation.Diagnostics));
        Assert.Empty(result.Calculation.Diagnostics);
        Assert.Equal(geometryCount, result.Calculation.Geometry.Length);
        Assert.Equal(geometryCount, result.Polygons.Length);
        Assert.All(result.Polygons, p => Assert.InRange(p.MaximumDeviationMm, 0m, .025m));
    }

    private static ArtifactSizes ArtifactMeasurements(InternalGeometryDocument document)
    {
        var imported = new ModelingImportDocument(1, document, new("synthetic-model", "synthetic-revision",
            "synthetic-result", "synthetic-owner", "synthetic-company", "synthetic-approver", DateTimeOffset.UnixEpoch));
        var configuration = new ModelingIdentityRemapper().Remap(imported);
        var configurationBytes = ModelingJson.Encode(configuration);
        var calculation = new GradingEngine().Calculate(configuration.Configuration, new(24, 20000));
        Assert.True(calculation.IsApprovable);
        var modelId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var revisionId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var resultId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var stored = new StoredGradingResult(revisionId, ModelingJson.Hash(configurationBytes), calculation);
        var approval = new GradingApprovalSnapshot(Guid.Parse("00000000-0000-0000-0000-000000000004"),
            resultId, modelId, "synthetic-approver", DateTimeOffset.UnixEpoch);
        var export = new ApprovedGradingDocument(1, modelId, revisionId, resultId, configuration, calculation, approval);
        var demands = calculation.Geometry.Take(100)
            .Select(g => new GradingNestingDemand(g.PieceId, g.SizeId, 10)).ToImmutableArray();
        var request = new GradingNestingPrepareRequest(Guid.NewGuid(), resultId, demands, 120, 30, [0, 90], false);
        var builder = new GradingNestingProblemBuilder(new GradingPolygonizer(new(24, 20000)));
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        var prepared = builder.Build(new("synthetic-company", "synthetic-owner"), Guid.NewGuid(), export, request);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new(ModelingJson.Encode(imported).Length, configurationBytes.Length,
            ModelingJson.Encode(stored).Length, ModelingJson.Encode(export).Length,
            ModelingJson.Encode(prepared).Length, elapsed, allocated, prepared.Request.Problem.Definitions.Count,
            prepared.Request.Problem.Instances.Count, prepared.Conversion.Sum(c => c.Segments.Length));
    }

    private static ProofWorkProbe ProbeProofWork(InternalGeometryDocument document)
    {
        var attempts = new List<ProofWorkAttempt>();
        foreach (var work in new[] { 1, 4, 16, 64, 256, 1024 })
        {
            var calculation = new GradingEngine().Calculate(document, new(24, work));
            attempts.Add(new(work, calculation.IsApprovable,
                calculation.Diagnostics.Select(d => d.Code).Distinct().Order().ToArray()));
            if (calculation.IsApprovable) return new(work, attempts.ToArray());
        }
        return new(null, attempts.ToArray());
    }

    private void WriteMeasurement(InternalGeometryDocument document, string shape, int repeat, Measurement measured,
        ArtifactSizes artifacts, ProofWorkProbe proofProbe)
    {
        var contours = document.Pieces.SelectMany(p => p.MaterialContours).ToArray();
        output.WriteLine(JsonSerializer.Serialize(new
        {
            measurement = "grading-operational-synthetic-v1", shape, repeat, warmups = 1,
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            processArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            processorCount = Environment.ProcessorCount,
            stopwatchFrequency = Stopwatch.Frequency, serverGc = System.Runtime.GCSettings.IsServerGC,
            algorithm = GradingPolygonizationLimits.AlgorithmVersion,
            proofBudget = new { maximumDepth = 24, maximumWork = 20000 },
            conversionBudget = new { maximumDepth = 24, configuredMaximumVertices = 20000, effectiveMaximumVertices = 200 },
            inputBytes = JsonSerializer.SerializeToUtf8Bytes(document).Length,
            importDocumentBytes = artifacts.ImportDocumentBytes,
            persistedConfigurationBytes = artifacts.PersistedConfigurationBytes,
            storedResultDocumentBytes = artifacts.StoredResultDocumentBytes,
            approvedExportDocumentBytes = artifacts.ApprovedExportDocumentBytes,
            preparedArtifactBytes = artifacts.PreparedArtifactBytes,
            preparationMilliseconds = artifacts.PreparationMilliseconds,
            preparationAllocatedBytes = artifacts.PreparationAllocatedBytes,
            preparationDefinitionCount = artifacts.PreparationDefinitionCount,
            preparationInstanceCount = artifacts.PreparationInstanceCount,
            preparationFinalVertices = artifacts.PreparationFinalVertices,
            preparationMeasurementScope = "single additional build; first up to 100 available piece-size pairs, quantity 10 each; serialization excluded",
            artifactMetadataScope = "synthetic fixed metadata; actual remapped identities and ModelingJson encoding",
            minimumSuccessfulTestedProofWork = proofProbe.MinimumSuccessfulTestedWork,
            proofWorkProbeAttempts = proofProbe.Attempts,
            proofWorkProbeScope = "discrete work probes at depth 24; not an exact minimum or a production limit",
            pieces = document.Pieces.Length, sizes = document.SizeSet.Sizes.Length,
            transitions = document.SizeSet.Sizes.Length - 1,
            controls = contours.Sum(c => c.Nodes.Count(n => n.IsGradingControl)),
            nodes = contours.Sum(c => c.Nodes.Length), segments = contours.Sum(c => c.Segments.Length),
            beziers = contours.Sum(c => c.Segments.Count(s => s.Kind == MaterialSegmentKind.CubicBezier)),
            controlSpans = contours.Sum(c => c.ControlSpans.Length), rules = document.Rules.Length,
            explicitZeroRules = document.Rules.Count(r => r.Displacement == default),
            derivedGeometry = measured.Result.Calculation.Geometry.Length,
            finalVerticesIncludingClosure = measured.Result.Polygons.Sum(p => p.Ring.Points.Count),
            finalEdges = measured.Result.Polygons.Sum(p => p.Segments.Length),
            maximumSubdivisionDepth = measured.Result.Polygons.SelectMany(p => p.Segments)
                .Max(s => s.SubdivisionPath.Length),
            certifiedMaximumBoundMm = measured.Result.Polygons.Max(p => p.MaximumDeviationMm),
            diagnostics = measured.Result.Calculation.Diagnostics.Length,
            engineMilliseconds = measured.EngineMilliseconds, conversionMilliseconds = measured.ConversionMilliseconds,
            engineAllocatedBytes = measured.EngineAllocatedBytes, conversionAllocatedBytes = measured.ConversionAllocatedBytes,
            allocationScope = "current-thread managed allocations; excludes native memory and peak RSS"
        }));
    }

    private sealed record Result(GradingCalculation Calculation, GradingPolygonizedContour[] Polygons);
    private sealed record Measurement(Result Result, double EngineMilliseconds, double ConversionMilliseconds,
        long EngineAllocatedBytes, long ConversionAllocatedBytes);
    private sealed record ArtifactSizes(int ImportDocumentBytes, int PersistedConfigurationBytes,
        int StoredResultDocumentBytes, int ApprovedExportDocumentBytes, int PreparedArtifactBytes,
        double PreparationMilliseconds, long PreparationAllocatedBytes, int PreparationDefinitionCount,
        int PreparationInstanceCount, int PreparationFinalVertices);
    private sealed record ProofWorkAttempt(int MaximumWork, bool IsApprovable, string[] DiagnosticCodes);
    private sealed record ProofWorkProbe(int? MinimumSuccessfulTestedWork, ProofWorkAttempt[] Attempts);
}
