using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Afterecho.Editor
{
    // Review evidence lives in the full JSON document, not the runtime chart DTO.
    public static class AfterechoChartReview
    {
        public static IEnumerable<JObject> Notes(JObject doc) => ((JArray)doc["notes"]).OfType<JObject>();
        public static double? SourceTime(JObject note)
        {
            if ((string)note["source"] == "manual_edit") return null;
            return Number(note["originalTime"]);
        }
        public static double? Number(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (!double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || double.IsNaN(value) || double.IsInfinity(value)) return null;
            return value;
        }
        static JObject ReviewRoot(JObject doc)
        {
            if (!(doc["review"] is JObject)) doc["review"] = new JObject();
            return (JObject)doc["review"];
        }
        static JArray History(JObject doc)
        {
            if (!(doc["humanReviews"] is JArray)) doc["humanReviews"] = new JArray();
            return (JArray)doc["humanReviews"];
        }
        public static JObject LatestRecord(JObject doc, JObject note)
        {
            string id = (string)note["id"];
            return (doc["humanReviews"] as JArray)?.OfType<JObject>().LastOrDefault(r => (string)r["noteId"] == id);
        }
        public static bool CurrentEvidence(JObject doc, JObject note)
        {
            var r = LatestRecord(doc, note);
            if (r == null || string.IsNullOrWhiteSpace((string)r["id"]) || string.IsNullOrWhiteSpace((string)note["humanReviewId"])
                || (string)r["id"] != (string)note["humanReviewId"]
                || Number(r["reviewedTime"]) != Number(note["time"])
                || Number(r["sourceTime"]) != SourceTime(note)
                || string.IsNullOrWhiteSpace((string)r["reviewer"])
                || string.IsNullOrWhiteSpace((string)r["reason"])
                || !DateTimeOffset.TryParse((string)r["reviewedUtc"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _)) return false;
            return true;
        }
        public static string State(JObject doc, JObject note)
        {
            var record = LatestRecord(doc, note);
            if (!CurrentEvidence(doc, note)) return "pending";
            string state = (string)record["decision"];
            if (state == "approved" && (bool?)record["humanConfirmed"] == true
                && (string)note["teamReview"] == "approved") return "approved";
            return state == "rejected" && (string)note["teamReview"] == "rejected" ? "rejected" : "pending";
        }
        public static double Window(JObject doc, JObject note)
        {
            var notes = Notes(doc).OrderBy(n => (double)n["time"]).ToArray();
            int i = Array.FindIndex(notes, n => (string)n["id"] == (string)note["id"]);
            if (i < 0) throw new InvalidOperationException("노트를 찾을 수 없습니다.");
            double left = i == 0 ? double.PositiveInfinity : (double)notes[i]["time"] - (double)notes[i - 1]["time"];
            double right = i + 1 == notes.Length ? double.PositiveInfinity : (double)notes[i + 1]["time"] - (double)notes[i]["time"];
            return Math.Min((double)doc["rules"]["maxWindow"], .4 * Math.Min(left, right));
        }
        public static bool LargeMove(JObject doc, JObject note)
        {
            var raw = SourceTime(note);
            return raw.HasValue && Math.Abs((double)note["time"] - raw.Value) >= Window(doc, note) - 1e-9;
        }
        static JObject Evidence(JObject note, string decision, string reviewer, string reason, string scope,
            bool confirmed, string phraseId = null, double? from = null, double? to = null)
        {
            double? source = SourceTime(note);
            return new JObject {
                ["id"] = Guid.NewGuid().ToString("N"), ["scope"] = scope, ["noteId"] = note["id"].DeepClone(),
                ["decision"] = decision, ["reviewer"] = reviewer, ["reason"] = reason,
                ["reviewedUtc"] = DateTimeOffset.UtcNow.ToString("o"), ["reviewedTime"] = (double)note["time"],
                ["previousTime"] = Number(note["parentTime"]), ["sourceTime"] = source,
                ["sourceTimeKnown"] = source.HasValue,
                ["sourceRetimed"] = source.HasValue ? (JToken)(Math.Abs((double)note["time"] - source.Value) > 1e-9) : JValue.CreateNull(),
                ["source"] = note["source"]?.DeepClone(), ["humanConfirmed"] = confirmed,
                ["previousReviewId"] = note["humanReviewId"]?.DeepClone(),
                ["phraseId"] = phraseId, ["from"] = from, ["to"] = to
            };
        }
        static void Attach(JObject doc, JObject note, JObject record)
        {
            History(doc).Add(record);
            note["humanReviewId"] = record["id"].DeepClone();
            note["teamReview"] = record["decision"].DeepClone();
            note["reviewStatus"] = (string)record["decision"] == "approved" ? "human_approved"
                : (string)record["decision"] == "rejected" ? "human_rejected" : "team_pending";
        }
        public static int Review(JObject doc, IEnumerable<string> ids, string decision, string reviewer,
            string reason, bool humanConfirmed, bool phrase = false, double? from = null, double? to = null)
        {
            if (!new[] { "pending", "approved", "rejected" }.Contains(decision)) throw new ArgumentException("검수 상태가 올바르지 않습니다.");
            if (string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("검수자와 구체적인 검수 이유를 입력하세요.");
            if (decision == "approved" && !humanConfirmed)
                throw new InvalidOperationException("직접 청음·입력과 시각을 확인한 뒤 확인란을 체크하세요. 자동 검사는 승인이 아닙니다.");
            var selected = new HashSet<string>(ids);
            var notes = Notes(doc).Where(n => selected.Contains((string)n["id"])).ToArray();
            if (notes.Length == 0 || notes.Length != selected.Count) throw new ArgumentException("검수할 현재 노트를 선택하세요.");
            string phraseId = phrase ? Guid.NewGuid().ToString("N") : null;
            foreach (var note in notes)
                Attach(doc, note, Evidence(note, decision, reviewer.Trim(), reason.Trim(), phrase ? "phrase" : "note", humanConfirmed, phraseId, from, to));
            RefreshSummary(doc);
            return notes.Length;
        }
        public static void Invalidate(JObject doc, JObject note, string reason)
        {
            Attach(doc, note, Evidence(note, "pending", "system:edit", reason, "edit", false));
            note["reviewStatus"] = "edited_pending";
        }
        static void Edit(JObject doc, JObject note, string action, double? before, double? after, string reason)
        {
            if (!(doc["edits"] is JArray)) doc["edits"] = new JArray();
            ((JArray)doc["edits"]).Add(new JObject {
                ["noteId"] = note["id"].DeepClone(), ["action"] = action, ["originalTime"] = before,
                ["revisedTime"] = after, ["sourceTime"] = SourceTime(note), ["parentTime"] = Number(note["parentTime"]),
                ["reason"] = string.IsNullOrWhiteSpace(reason) ? "manual edit; listening review pending" : reason.Trim(),
                ["timestampUtc"] = DateTimeOffset.UtcNow.ToString("o"), ["reviewStatus"] = "team_pending"
            });
        }
        public static void Shift(JObject doc, JObject note, double at, string reason)
        {
            if (double.IsNaN(at) || double.IsInfinity(at) || at < 0 || at > (double)doc["song"]["duration"])
                throw new ArgumentException("시각은 곡 길이 안의 유한한 숫자여야 합니다.");
            double before = (double)note["time"];
            if (Math.Abs(before - at) < 1e-9) return;
            Edit(doc, note, "shift", before, at, reason);
            note["time"] = at;
            Invalidate(doc, note, "timing changed; re-review required");
            RefreshSummary(doc);
        }
        public static void Delete(JObject doc, JObject note, string reason)
        {
            Edit(doc, note, "delete", (double)note["time"], null, reason);
            Invalidate(doc, note, "note deleted; prior approval invalidated");
            note.Remove();RefreshSummary(doc);
        }
        public static JObject Add(JObject doc, double at, string reason)
        {
            if (double.IsNaN(at) || double.IsInfinity(at) || at < 0 || at > (double)doc["song"]["duration"])
                throw new ArgumentException("시각은 곡 길이 안의 유한한 숫자여야 합니다.");
            var n = new JObject { ["id"] = "EDIT-" + Guid.NewGuid().ToString("N").Substring(0, 12), ["time"] = at,
                ["originalTime"] = at, ["source"] = "manual_edit", ["reviewStatus"] = "edited_pending", ["teamReview"] = "pending" };
            ((JArray)doc["notes"]).Add(n);Edit(doc, n, "add", null, at, reason);RefreshSummary(doc);return n;
        }
        public static void RefreshSummary(JObject doc)
        {
            var notes = Notes(doc).ToArray();
            foreach (var n in notes)
            {
                n["teamReview"] = State(doc, n);
                double? raw = SourceTime(n);n["sourceTimeKnown"] = raw.HasValue;
                n["sourceRetimed"] = raw.HasValue ? (JToken)(Math.Abs((double)n["time"] - raw.Value) > 1e-9) : JValue.CreateNull();
            }
            int approved = notes.Count(n => (string)n["teamReview"] == "approved");
            int rejected = notes.Count(n => (string)n["teamReview"] == "rejected");
            var review = ReviewRoot(doc);
            review["musicalAndOneHand"] = notes.Length > 0 && approved == notes.Length ? "human_review_complete" : "team_pending";
            review["approvedNotes"] = approved;review["rejectedNotes"] = rejected;review["pendingNotes"] = notes.Length - approved - rejected;
            review["trainingEligible"] = false;
            review["trainingGate"] = "Explicit separate training approval required, even after every note is reviewed.";
        }
        public sealed class Comparison
        {
            public double from, to;
            public string[] noteIds;
            public double[] sourceTimes, currentTimes;
            public int manualExcluded;
        }
        public static Comparison BuildComparison(JObject doc, double from, double to)
        {
            if (double.IsNaN(from) || double.IsInfinity(from) || double.IsNaN(to) || double.IsInfinity(to)
                || from < 0 || to <= from || to > (double)doc["song"]["duration"])
                throw new ArgumentException("비교 구간은 곡 길이 안에서 지정하세요.");
            var selected = Notes(doc).Where(n => (double)n["time"] >= from && (double)n["time"] < to).ToArray();
            var notes = selected.Where(n => SourceTime(n).HasValue).ToArray();
            if (notes.Length == 0) throw new ArgumentException("현재 구간에는 비교할 원시 AI 시각이 있는 노트가 없습니다.");
            double duration = (double)doc["song"]["duration"];
            var raw = notes.Select(n => SourceTime(n).Value).OrderBy(t => t).ToArray();
            var current = notes.Select(n => (double)n["time"]).OrderBy(t => t).ToArray();
            if (raw.Any(t => t < 0 || t > duration)) throw new ArgumentException("원시 시각이 곡 범위를 벗어났습니다. 출처를 확인하세요.");
            return new Comparison { from = Math.Min(from, Math.Min(raw[0], current[0])),
                to = Math.Min(duration, Math.Max(to, Math.Max(raw[raw.Length - 1], current[current.Length - 1]) + .03)),
                noteIds = notes.Select(n => (string)n["id"]).ToArray(), sourceTimes = raw, currentTimes = current,
                manualExcluded = selected.Length - notes.Length };
        }
        public static double[] ComparisonTimes(JObject doc, bool source, double from, double to)
        { var selection = BuildComparison(doc, from, to);return source ? selection.sourceTimes : selection.currentTimes; }
    }
}
