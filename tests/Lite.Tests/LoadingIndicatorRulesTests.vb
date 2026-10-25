Option Explicit On
Option Strict On
Option Infer On
Option Compare Binary

Imports System
Imports Xunit

Namespace fmsl

    <Collection("LocalizationState")>
    Public Class LoadingIndicatorRulesTests

        <Fact>
        Public Sub ComputePercent_CalculatesAccurately()
            Assert.Equal(0, LoadingIndicatorRules.ComputePercent(0, 1000))
            Assert.Equal(0, LoadingIndicatorRules.ComputePercent(-5, 1000))
            Assert.Equal(0, LoadingIndicatorRules.ComputePercent(100, 0))
            Assert.Equal(0, LoadingIndicatorRules.ComputePercent(100, -100))

            Assert.Equal(25, LoadingIndicatorRules.ComputePercent(250, 1000))
            Assert.Equal(50, LoadingIndicatorRules.ComputePercent(500, 1000))
            Assert.Equal(99, LoadingIndicatorRules.ComputePercent(999, 1000))
            Assert.Equal(100, LoadingIndicatorRules.ComputePercent(1000, 1000))
            Assert.Equal(100, LoadingIndicatorRules.ComputePercent(1500, 1000))
        End Sub

        <Fact>
        Public Sub EstimateRemainingSeconds_CalculatesAccurately()
            ' Less than 1 second elapsed -> not ready to estimate
            Assert.Equal(-1, LoadingIndicatorRules.EstimateRemainingSeconds(100, 1000, 0.5))

            ' Non-positive or finished transfers
            Assert.Equal(-1, LoadingIndicatorRules.EstimateRemainingSeconds(0, 1000, 2.0))
            Assert.Equal(-1, LoadingIndicatorRules.EstimateRemainingSeconds(100, 0, 2.0))
            Assert.Equal(0, LoadingIndicatorRules.EstimateRemainingSeconds(1000, 1000, 2.0))
            Assert.Equal(0, LoadingIndicatorRules.EstimateRemainingSeconds(1200, 1000, 2.0))

            ' 200 bytes in 2 seconds = 100 bytes/sec. Remaining 800 bytes = 8 seconds.
            Assert.Equal(8, LoadingIndicatorRules.EstimateRemainingSeconds(200, 1000, 2.0))

            ' 40 MB total (40000000 B), 10 MB loaded (10000000 B) in 5 seconds = 2 MB/s. Remaining 30 MB = 15 s.
            Assert.Equal(15, LoadingIndicatorRules.EstimateRemainingSeconds(10000000L, 40000000L, 5.0))
        End Sub

        <Fact>
        Public Sub FormatRemainingTime_FormatsCorrectly()
            Dim origLang As String = Localization.CurrentCode
            Try
                Localization.CurrentCode = "ru"
                Assert.Equal("", LoadingIndicatorRules.FormatRemainingTime(-1))
                Assert.Equal("~0 с", LoadingIndicatorRules.FormatRemainingTime(0))
                Assert.Equal("~13 с", LoadingIndicatorRules.FormatRemainingTime(13))
                Assert.Equal("~1:05", LoadingIndicatorRules.FormatRemainingTime(65))
                Assert.Equal("~10:00", LoadingIndicatorRules.FormatRemainingTime(600))
                Assert.Equal("~1:01:05", LoadingIndicatorRules.FormatRemainingTime(3665))

                Localization.CurrentCode = "en"
                Assert.Equal("~13 s", LoadingIndicatorRules.FormatRemainingTime(13))
            Finally
                Localization.CurrentCode = origLang
            End Try
        End Sub

        <Fact>
        Public Sub FormatLoadingSize_FormatsBinaryUnits()
            Dim origLang As String = Localization.CurrentCode
            Try
                Localization.CurrentCode = "ru"
                Assert.Equal("500 Б", LoadingIndicatorRules.FormatLoadingSize(500L))
                Assert.Equal("1.5 КиБ", LoadingIndicatorRules.FormatLoadingSize(1536L))
                Assert.Equal("40.3 МиБ", LoadingIndicatorRules.FormatLoadingSize(42257613L))

                Localization.CurrentCode = "en"
                Assert.Equal("500 B", LoadingIndicatorRules.FormatLoadingSize(500L))
                Assert.Equal("1.5 KiB", LoadingIndicatorRules.FormatLoadingSize(1536L))
                Assert.Equal("40.3 MiB", LoadingIndicatorRules.FormatLoadingSize(42257613L))
            Finally
                Localization.CurrentCode = origLang
            End Try
        End Sub

        <Fact>
        Public Sub UpdateMetricsOncePerSecond_OnlyRecalculatesWhenSecondRollsOver()
            Dim lastSec As Integer = -1
            Dim cachedPct As Integer = 0
            Dim cachedRemSec As Integer = -1

            ' Initial calculation at 250ms (second 0)
            LoadingIndicatorRules.UpdateMetricsOncePerSecond(
                0L, 1000L, TimeSpan.FromMilliseconds(250), lastSec, cachedPct, cachedRemSec)
            Assert.Equal(0, lastSec)
            Assert.Equal(0, cachedPct)
            Assert.Equal(-1, cachedRemSec)

            ' Subsequent frame at 500ms (still second 0) - should NOT recalculate even if loadedBytes changed
            LoadingIndicatorRules.UpdateMetricsOncePerSecond(
                200L, 1000L, TimeSpan.FromMilliseconds(500), lastSec, cachedPct, cachedRemSec)
            Assert.Equal(0, lastSec)
            Assert.Equal(0, cachedPct) ' remains cached 0

            ' Second rollover at 1100ms (second 1) - recalculates!
            LoadingIndicatorRules.UpdateMetricsOncePerSecond(
                200L, 1000L, TimeSpan.FromMilliseconds(1100), lastSec, cachedPct, cachedRemSec)
            Assert.Equal(1, lastSec)
            Assert.Equal(20, cachedPct)
            Assert.True(cachedRemSec > 0)
        End Sub

    End Class

End Namespace
