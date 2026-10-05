using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace LotamSentinel.Application.Ingestion;

/// <summary>
/// Parses a SEC LOTAM PDF into <see cref="ParsedRelease"/>.
///
/// REVISION HISTORY / WHY THIS LOOKS THE WAY IT DOES:
///
///   1-3. (earlier fixes, kept): 4.0pt line-grouping Y-tolerance; material
///        code/Total regexes tolerant of fragmented digit/period tokens;
///        content-pattern entry-boundary detection instead of geometric,
///        since the source PDF has no ruling lines between manufacturer
///        rows and one-page-per-entry does not hold.
///
///   4. (earlier fix, kept): COLUMN MIXING - each row is split into its 5
///      column buckets by X-position BEFORE any text is accumulated,
///      instead of routing a whole row's text (which can span multiple
///      columns at once) into one bucket based on the leftmost word.
///
///   5. THIS REVISION - MULTIPLE ENTRIES MERGED INTO ONE ROW: confirmed
///      directly against material 52.2 in the real Version B PDF - "Hitachi
///      Energy" and "Grid Solutions Oy" were both being silently appended
///      as continuation text into whichever entry was already open, because
///      the only "new entry while already inside one" trigger was "first
///      word is ALL CAPS" (catches "MIDAL CABLE", "NARI Technology..." but
///      not Title-Case names like "Hitachi Energy"). Loosening that check
///      to "any capitalized word" was tried and rejected - ordinary address
///      continuation lines ("Smart Grid Industrial Building No 16,") are
///      ALSO Title Case and don't match any continuation-prefix blacklist
///      term, so that just misfires constantly in the other direction.
///
///      The real, reliable signal is the document's own pattern: a genuine
///      new entry's name is followed within 1-2 lines by a recognized
///      country name; address continuation lines never are. Detecting that
///      requires lookahead, which a single forward pass over the whole
///      document can't do cheaply. Fix: body rows are now extracted (with
///      their per-column text already split out, per fix #4) into a
///      per-SECTION list first; only once a section closes (its "Total ="
///      line is found) does a second step walk that bounded, in-memory row
///      list with 1-2 row lookahead to decide entry boundaries.
///
///      The lookahead itself needed guarding: a country can follow a row
///      for reasons unrelated to a new entry starting - confirmed directly
///      against the same section, where a wrapped email fragment
///      ("lixg@nrec.com" on its own line), a wrapped street address
///      ("Vehmaistenkatu 5, 33730 Tampere,"), and a wrapped lowercase URL
///      tail ("ons/") were each followed by a country shortly after and
///      misdetected as new entries. LooksLikeNameCandidate excludes
///      anything containing '@' or a digit, and requires an uppercase
///      first letter, before either trigger (all-caps or lookahead) is
///      considered. Verified against the FULL material 52.2 section in the
///      real Version B PDF: correctly separates all 6 manufacturers (NR
///      Electric, Rongxin Huiko, NARI Technology, Hitachi Energy, Grid
///      Solutions Oy, HYOSUNG) - matching SEC's printed "Total = 6"
///      exactly, with the right names in the right entries, not just the
///      right count.
/// </summary>
public class PdfLotamParser
{
    private const double ColManufacturerStart = 42;
    private const double ColLocalRepStart = 272;
    private const double ColModelRatingStart = 375;
    private const double ColTypeTestsStart = 533;
    private const double ColRemarksStart = 621;
    private const double MarginNoiseX = 45;

    private static readonly Regex MaterialCodeLine = new(@"^\s*(\d+)\s*\.\s*(\d+)\s*$", RegexOptions.Compiled);
    private static readonly Regex TotalLine = new(@"Total\s*=\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex HeaderDateLine = new(@"(\d{1,2})\s*/\s*(\d{2})\s*/\s*(\d{4})", RegexOptions.Compiled);

    private static readonly string[] HeaderBandTokens =
    {
        "SER. MATERIAL", "EQUIPMENT", "DESCRIPTION", "MANUFACTURER NAME",
        "PREQUALIFIED PLANT", "AUTHORIZED LOCAL", "REPRESENTATIVE",
        "TYPE/MODEL AND RATING", "TYPE TESTS", "REMARKS/OBSERVATIONS",
        "LOCATION,CITY", "COMPLETE ADDRESS", "WEBSITE, TELEPHONE",
        "ETC. AND NAME", "DETAILS (DATE", "LABORATORY,", "LOCATION AND",
        "REPORT NUMBER", "ADDRESS,", "NAME,TELEPHONE"
    };

    private static readonly string[] ContinuationPrefixes =
    {
        "contact person", "phone", "email id", "email", "website", "p.o", "po box",
        "plant -", "plant:", "tel:", "tel :", "fax", "http://", "https://",
        "general manager", "sale manager", "key account", "manager", "mr.", "mrs.", "dr."
    };

    private static readonly HashSet<string> CountryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "saudi arabia", "bahrain", "india", "united states", "oman", "france", "china",
        "south korea", "korea", "sweden", "finland", "germany", "switzerland", "uae",
        "united arab emirates", "italy", "japan", "canada", "united kingdom", "uk", "usa",
        "spain", "turkey", "egypt", "kuwait", "qatar", "jordan", "austria", "belgium",
        "netherlands", "czech republic", "poland", "hungary", "malaysia", "indonesia",
        "thailand", "vietnam", "taiwan", "brazil", "mexico", "australia", "norway",
        "korea, republic of", "republic of korea",
        "denmark", "portugal", "greece", "russia", "south africa", "singapore",
        "philippines", "pakistan", "bangladesh", "iran", "iraq", "lebanon", "syria",
        "israel", "new zealand", "ireland", "romania", "bulgaria", "croatia", "slovakia",
        "slovenia", "ukraine"
    };

    private enum EntryState { Waiting, InName, InBody }

    // One body row, already split into its 5 column texts (fix #4), waiting
    // to be assigned to an entry once the section closes and entry
    // boundaries can be resolved with lookahead.
    private sealed record RawRow(int PageNum, string Manu, string Rep, string Model, string Tests, string Remarks);

    public ParsedRelease Parse(string filePath)
    {
        var result = new ParsedRelease();
        using var document = PdfDocument.Open(filePath);
        result.PageCount = document.NumberOfPages;

        ParsedSection? currentSection = null;
        var pendingRows = new List<RawRow>();
        string currentCategory = "UNKNOWN";
        var sectionDescriptionBuffer = new StringBuilder();
        bool collectingDescription = false;

        void CloseSection(int printedTotal, int endedOnPage)
        {
            if (currentSection is null) return;
            currentSection.PrintedTotal = printedTotal;
            currentSection.LastPageNumber = endedOnPage;
            currentSection.Entries.AddRange(SegmentEntries(pendingRows));
            result.Sections.Add(currentSection);
            pendingRows = new List<RawRow>();
        }

        for (int pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
        {
            var page = document.GetPage(pageNum);
            if (pageNum == 1) result.ReleaseDate = TryExtractHeaderDate(page);

            foreach (var line in GroupWordsIntoLines(page))
            {
                var fullText = string.Join(" ", line.Select(w => w.Text)).Trim();
                if (fullText.Length == 0) continue;
                if (fullText.Contains("Page") && fullText.Contains("of")) continue;

                var topFromPageTop = page.Height - line[0].BoundingBox.Top;

                // CRITICAL: the header-band check below used to match on TEXT
                // CONTENT alone ("EQUIPMENT", "DESCRIPTION", etc. appearing
                // anywhere in the line). Those are ordinary English words that
                // also appear inside genuine content - confirmed directly:
                // the real category "SAFETY AND SECURITY EQUIPMENT" was being
                // discarded because it contains "EQUIPMENT", and a remarks
                // line reading "...equipment to be supplied to SEC/National
                // Grid SA." was discarded the same way, which is exactly
                // where a real remarks paragraph was cutting off mid-sentence.
                // Fix: the repeating header band only ever physically renders
                // within the first ~90pt of a page (confirmed across many
                // sampled pages: header rows sit at top~22 through ~75) - so
                // gate the token match on vertical position too. Real content
                // (category headers, remarks, everything) starts well below
                // that, so this can't falsely catch it.
                if (topFromPageTop < 90 && IsHeaderBandLine(fullText)) continue;

                var leftMost = line.Min(w => w.BoundingBox.Left);

                var totalMatch = TotalLine.Match(fullText);
                if (totalMatch.Success && currentSection is not null)
                {
                    CloseSection(int.Parse(totalMatch.Groups[1].Value, CultureInfo.InvariantCulture), pageNum);
                    currentSection = null;
                    collectingDescription = false;
                    continue;
                }

                var codeMatch = MaterialCodeLine.Match(fullText);
                if (codeMatch.Success)
                {
                    // Section ended without a Total line being found (shouldn't
                    // normally happen in a well-formed document) - close it with
                    // whatever was accumulated rather than losing the data.
                    if (currentSection is not null)
                        CloseSection(pendingRows.Count(r => r.Manu.Length > 0), pageNum);

                    currentSection = new ParsedSection
                    {
                        CategoryName = currentCategory,
                        Code = $"{codeMatch.Groups[1].Value}.{codeMatch.Groups[2].Value}",
                        RawDescription = string.Empty,
                        FirstPageNumber = pageNum
                    };
                    collectingDescription = true;
                    sectionDescriptionBuffer.Clear();
                    continue;
                }

                if (leftMost < MarginNoiseX) continue;

                if (collectingDescription && currentSection is not null)
                {
                    var stdIdx = fullText.IndexOf("Applicable Standards:", StringComparison.OrdinalIgnoreCase);
                    if (stdIdx >= 0)
                    {
                        sectionDescriptionBuffer.Append(fullText[..stdIdx]);
                        currentSection.RawDescription = sectionDescriptionBuffer.ToString().Trim();
                        currentSection.ApplicableStandard = fullText[(stdIdx + "Applicable Standards:".Length)..].Trim();
                        collectingDescription = false;
                    }
                    else
                    {
                        sectionDescriptionBuffer.Append(fullText).Append(' ');
                    }
                    continue;
                }

                // Category headers print in the left margin (x0~62, confirmed
                // across every sampled instance) - well left of real body
                // content (x0~113+). Only checked here (currentSection is
                // null = between sections), so this can't be confused with
                // body rows.
                if (currentSection is null && leftMost < 100 && LooksLikeCategoryHeader(fullText))
                {
                    currentCategory = fullText.Trim();
                    continue;
                }

                if (currentSection is null) continue;

                // Split by column (fix #4), queue for entry segmentation once
                // the section closes (fix #5) instead of deciding immediately.
                pendingRows.Add(new RawRow(
                    pageNum,
                    JoinWords(line, w => w.BoundingBox.Left < ColLocalRepStart),
                    JoinWords(line, w => w.BoundingBox.Left >= ColLocalRepStart && w.BoundingBox.Left < ColModelRatingStart),
                    JoinWords(line, w => w.BoundingBox.Left >= ColModelRatingStart && w.BoundingBox.Left < ColTypeTestsStart),
                    JoinWords(line, w => w.BoundingBox.Left >= ColTypeTestsStart && w.BoundingBox.Left < ColRemarksStart),
                    JoinWords(line, w => w.BoundingBox.Left >= ColRemarksStart)));
            }
        }

        // Trailing section with no Total line at end of document (shouldn't
        // normally happen, but don't silently drop the data if it does).
        if (currentSection is not null)
            CloseSection(pendingRows.Count(r => r.Manu.Length > 0), document.NumberOfPages);

        return result;
    }

    /// <summary>
    /// Turns one section's flat row list into ParsedEntry objects. This is
    /// where fix #5 lives: LooksLikeNewEntryStart has 1-2 rows of lookahead
    /// available because `rows` is a bounded, already-fully-collected list
    /// for just this section - unlike the old single forward pass, which
    /// could only ever look at the current row.
    /// </summary>
    private static List<ParsedEntry> SegmentEntries(List<RawRow> rows)
    {
        var entries = new List<ParsedEntry>();
        var manuBuf = new StringBuilder();
        var repBuf = new StringBuilder();
        var modelBuf = new StringBuilder();
        var testsBuf = new StringBuilder();
        var remarksBuf = new StringBuilder();
        var state = EntryState.Waiting;
        int entryStartPage = 0;
        // Row-count budget rather than an open-ended boolean - confirmed
        // real case: an unbounded suppression let a short secondary-address
        // block ("Plant -Dubai." + one more line, Fike Corporation,
        // material 45.3) run all the way to a page boundary and swallow the
        // next genuinely new manufacturer. Real secondary-address blocks are
        // 2-4 lines, so this budget covers them while bounding the worst case.
        const int SuppressionMaxRows = 4;
        int suppressingRowsLeft = 0;
        bool suppressOneMore = false;

        void Flush()
        {
            if (state == EntryState.Waiting) return;
            var rawBlock = manuBuf.ToString().Trim('\n', '\r', ' ');
            if (rawBlock.Length == 0)
            {
                state = EntryState.Waiting;
                return;
            }

            var lines = rawBlock.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            var firstLine = lines.FirstOrDefault() ?? "UNKNOWN MANUFACTURER";
            var countryGuess = lines.FirstOrDefault(IsCountryLineBroad);

            entries.Add(new ParsedEntry
            {
                OrdinalInSection = entries.Count + 1,
                PageNumber = entryStartPage,
                RawManufacturerBlock = rawBlock,
                ManufacturerCountryRaw = countryGuess,
                GuessedManufacturerName = firstLine,
                LocalRepresentativeRaw = NullIfEmpty(repBuf.ToString()),
                ModelRatingRaw = NullIfEmpty(modelBuf.ToString()),
                TypeTestsRaw = ReflowParagraphs(NullIfEmpty(testsBuf.ToString())),
                RemarksRaw = ReflowParagraphs(NullIfEmpty(remarksBuf.ToString()))
            });

            manuBuf.Clear(); repBuf.Clear(); modelBuf.Clear(); testsBuf.Clear(); remarksBuf.Clear();
            state = EntryState.Waiting;
            suppressingRowsLeft = 0; // stale once the entry that set it is finalized
            suppressOneMore = false;
        }

        // Looks ahead (skipping rows with no manufacturer-column text at all)
        // for a country name within the next 1-2 candidate lines, stopping
        // early if a continuation-prefix line (Contact Person/Phone/Website/
        // etc.) is hit first - that means we've run past where a name would
        // end without finding a country, so this isn't a new-name pattern.
        // Window tightened from 3 to 2: every confirmed real case (Hitachi
        // Energy -> SWEDEN, Grid Solutions Oy -> FINLAND) only ever needed 1
        // step. A window of 3 was wide enough to reach past a short entry
        // into the FOLLOWING manufacturer's own country line, which is
        // exactly what caused a real false split - confirmed against
        // material 10.2: "Oberentfelden Switzerland" (GE GRID GMBH's own
        // address, not a new company) was misdetected as a new entry only
        // because a window of 3 could reach HYOSUNG's "SOUTH KOREA" two
        // rows later. Also fixed at the root by IsCountryLineBroad (below)
        // recognizing "Oberentfelden Switzerland" as a country/address line
        // in its own right, so it's excluded from candidate status entirely
        // rather than relying on window size alone.
        bool CountryFollowsWithinLookahead(int fromIndex)
        {
            int checkedCount = 0;
            for (int j = fromIndex + 1; j < rows.Count && checkedCount < 2; j++)
            {
                var text = rows[j].Manu;
                if (text.Length == 0) continue;
                checkedCount++;
                if (IsCountryLineStrict(text)) return true;
                if (IsContinuationPrefix(text)) return false;
            }
            return false;
        }

        bool LooksLikeNewEntryStart(int index, bool isContinuationLabel, bool isCountry)
        {
            var text = rows[index].Manu;
            if (isContinuationLabel || isCountry) return false;
            if (!LooksLikeNameCandidate(text)) return false;
            if (ContainsSecondaryAddressMarker(text)) return false;
            if (StartsWithAllCapsWord(text)) return true; // "MIDAL CABLE", "NARI Technology..."
            return CountryFollowsWithinLookahead(index); // "Hitachi Energy", "Grid Solutions Oy"
        }

        // Suppresses new-entry detection for every line following a
        // secondary plant/location marker, until a genuine country line or
        // continuation-prefix (Contact Person/Phone/Website/etc.) signals
        // we've reached the entry's real contact block. Needed because a
        // secondary plant address is usually 2-4 lines (marker, street,
        // city/country fragment), not just the one line ContainsSecondaryAddressMarker
        // catches - confirmed real case: material 1.1, APAR INDUSTRIES'
        // "SILVASSA Plant: 2010." marker line alone wasn't enough; the
        // street-address lines immediately after it were still being
        // misdetected as their own new entries via the lookahead trigger.

        // A "Contact Person - ..." (or Phone/Website/etc.) line sometimes
        // wraps across 2+ physical rows, and the wrapped continuation can
        // itself start with a short all-caps job-title abbreviation ("CEO",
        // "COO") that would otherwise pass the all-caps new-entry trigger.
        // Confirmed real case: "Contact Person - Mr. Kurian Mathew -" wraps
        // into "CEO, Robben Zacharia - Sales" / "Manager" - a trailing dash
        // on the continuation-prefix line is the real document's own signal
        // that more text follows, so it's used here to suppress exactly the
        // next row (not an open-ended block, unlike the plant/address case
        // below - this wrapping is reliably just one more row).
        static bool EndsWithContinuationDash(string text) => text.TrimEnd().EndsWith('-');

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            if (row.Manu.Length > 0)
            {
                var isContinuationLabel = IsContinuationPrefix(row.Manu);
                var isCountry = IsCountryLineBroad(row.Manu);
                var isSecondaryAddressMarker = ContainsSecondaryAddressMarker(row.Manu);

                switch (state)
                {
                    case EntryState.Waiting:
                        if (!isContinuationLabel && !isCountry && HasEnoughLetters(row.Manu) && LooksLikeNameCandidate(row.Manu))
                        {
                            entryStartPage = row.PageNum;
                            manuBuf.Append(row.Manu).Append('\n');
                            state = EntryState.InName;
                        }
                        break;

                    case EntryState.InName:
                        manuBuf.Append(row.Manu).Append('\n');
                        if (isContinuationLabel || isCountry)
                        {
                            state = EntryState.InBody;
                            if (isContinuationLabel && !isSecondaryAddressMarker && EndsWithContinuationDash(row.Manu)) suppressOneMore = true;
                        }
                        break;

                    case EntryState.InBody:
                        // Priority matters here and was the source of two
                        // real bugs before this ordering: a genuine country
                        // (isCountry) must ALWAYS end suppression immediately,
                        // even on a line that also happens to contain a plant
                        // marker (confirmed real case: material 27.2's
                        // "Plant -Zaporizhia , Ukraine") - otherwise a real
                        // next manufacturer gets swallowed. And the
                        // secondary-address marker must be checked BEFORE the
                        // plain continuation-prefix check, because "plant -"
                        // is (confusingly) also one of the continuation
                        // prefixes - a marker line with no country of its own
                        // (confirmed real case: material 1.5/GANSEC's
                        // "Plant - NO:8, LANE 24, QIANSHI") needs the marker
                        // branch to win so the wrapped address that follows
                        // stays protected.
                        if (suppressOneMore)
                        {
                            manuBuf.Append(row.Manu).Append('\n');
                            suppressOneMore = false;
                        }
                        else if (isCountry)
                        {
                            manuBuf.Append(row.Manu).Append('\n');
                            suppressingRowsLeft = 0;
                        }
                        else if (isSecondaryAddressMarker)
                        {
                            manuBuf.Append(row.Manu).Append('\n');
                            suppressingRowsLeft = SuppressionMaxRows;
                        }
                        else if (isContinuationLabel)
                        {
                            manuBuf.Append(row.Manu).Append('\n');
                            suppressingRowsLeft = 0;
                            if (EndsWithContinuationDash(row.Manu)) suppressOneMore = true;
                        }
                        else if (suppressingRowsLeft > 0)
                        {
                            manuBuf.Append(row.Manu).Append('\n');
                            suppressingRowsLeft--;
                        }
                        else if (LooksLikeNewEntryStart(i, isContinuationLabel, isCountry))
                        {
                            Flush();
                            entryStartPage = row.PageNum;
                            manuBuf.Append(row.Manu).Append('\n');
                            state = EntryState.InName;
                        }
                        else
                        {
                            manuBuf.Append(row.Manu).Append('\n');
                        }
                        break;
                }
            }

            if (state != EntryState.Waiting)
            {
                if (row.Rep.Length > 0) repBuf.Append(row.Rep).Append('\n');
                if (row.Model.Length > 0) modelBuf.Append(row.Model).Append('\n');
                if (row.Tests.Length > 0) testsBuf.Append(row.Tests).Append('\n');
                if (row.Remarks.Length > 0) remarksBuf.Append(row.Remarks).Append('\n');
            }
        }

        Flush();
        return entries;
    }

    private static string JoinWords(List<Word> line, Func<Word, bool> predicate)
        => string.Join(" ", line.Where(predicate).Select(w => w.Text)).Trim();

    /// <summary>
    /// Remarks/Type-Tests text is stored one raw line per source row, but a
    /// single sentence routinely wraps across many rows (confirmed directly:
    /// "Based on S&SD letter# MPQ676321653 dated" / "November 12, 2020, the
    /// manufacturer is prequalified" / ... is one sentence split across 6
    /// rows). Rendering every row as its own line made one paragraph look
    /// like six. This reflows wrapped lines back into paragraphs: a line
    /// joins the previous one with a space UNLESS it's a recognized section
    /// label ("WARRANTY", "TYPE TESTS", "PQ LETTER", "Others", "Condition:",
    /// etc. - confirmed as the real repeating labels in this column), which
    /// starts a new paragraph instead. Best-effort, like the other content
    /// heuristics in this file - the raw per-row data isn't lost, only this
    /// derived reflow can be wrong.
    /// </summary>
    private static string? ReflowParagraphs(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        var lines = raw.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var paragraphs = new List<string>();
        var current = new StringBuilder();

        void FlushParagraph()
        {
            if (current.Length > 0) paragraphs.Add(current.ToString());
            current.Clear();
        }

        foreach (var line in lines)
        {
            if (IsRemarksLabelLine(line))
            {
                FlushParagraph();
                paragraphs.Add(line);
                continue;
            }

            if (current.Length == 0) current.Append(line);
            else current.Append(' ').Append(line);
        }
        FlushParagraph();

        return string.Join('\n', paragraphs);
    }

    private static bool IsRemarksLabelLine(string line)
    {
        var t = line.TrimEnd(':').Trim();
        if (t.Length == 0 || t.Length > 30) return false;
        if (t.Any(char.IsDigit)) return false;
        var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 3) return false;
        return words.All(w => w.Length > 0 && char.IsUpper(w[0]));
    }

    private static bool IsContinuationPrefix(string text)
    {
        var low = text.TrimStart().ToLowerInvariant();
        return ContinuationPrefixes.Any(p => low.StartsWith(p, StringComparison.Ordinal));
    }

    // Matches either a line that IS just the country ("SWEDEN") or one that
    // ENDS with a recognized country ("Oberentfelden Switzerland" - a real
    // city+country address line). The end-with check covers 1- and 2-word
    // country names (e.g. "South Korea"). Confirmed necessary: without it, a
    // manufacturer whose only country line is written as "City, Country"
    // could get its own address line misdetected as a new entry (see
    // material 10.2 fix notes above) since it never matched the whole-line
    // check but also didn't look like a normal continuation line.
    //
    // Deliberately NOT also matching "starts with a country" - tried that
    // and reverted it: "OMAN CABLE INDUSTRIES" starts with "Oman", a real
    // country, and country-as-company-name-prefix is a common, real pattern
    // (Oman Cable, India Cements, France Telecom, etc.) - a starts-with rule
    // broke that manufacturer's own name from ever being recognized.
    //
    // This BROAD version is used for state-machine gating (is the CURRENT
    // row itself a country/address line, blocking it from candidate status).
    // The lookahead below uses the separate STRICT version - see its own
    // comment for why those needed to differ.
    private static bool IsCountryLineBroad(string text)
    {
        var trimmed = text.Trim().TrimEnd('.', ',');
        if (CountryNames.Contains(trimmed)) return true;

        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2 && CountryNames.Contains(string.Join(' ', words[^2..]))) return true;
        if (words.Length >= 1 && CountryNames.Contains(words[^1])) return true;
        return false;
    }

    // Exact whole-line match only - deliberately stricter than the broad
    // version above. Used only by the lookahead scan, which asks "does a
    // clean, standalone country line follow soon" as a positive signal for
    // a new entry. The broad (ends-with) version is too easily satisfied by
    // an ordinary address line that merely ends in a country word - confirmed
    // real false positive: "Mannheim, Germany , Brown Boveri" (continuation
    // of ABB AG's own entry) was immediately followed by another address
    // line ending in "...Hannau, Germany", which the broad check treated as
    // "a country follows", incorrectly splitting ABB's own address into a
    // second phantom entry. The lookahead needs the stronger, narrower
    // signal: the next line actually IS just a country, nothing else.
    private static bool IsCountryLineStrict(string text)
        => CountryNames.Contains(text.Trim().TrimEnd('.', ','));

    // Catches secondary address blocks embedded within one manufacturer's
    // own entry - two confirmed real patterns: "SILVASSA Plant: 2010."
    // (a second plant listing) and a numbered address list like
    // "1. Business Communication address:" / "2. Plant Address:" /
    // "3. Branch Address:" (confirmed real case: GANSEC SHENGYUEAN WIRE &
    // CABLE CO. LTD.'s entry lists three addresses this way, and place-name
    // fragments within them - "CHONGFU TOWN DEVELOPMENT", "SONGJIANG
    // DISTRICT, SHANGHAI" - were each individually triggering a false new
    // entry before this was added).
    private static readonly Regex NumberedAddressLabel = new(@"^\s*\d+\.\s*.*address", RegexOptions.IgnoreCase);

    private static bool ContainsSecondaryAddressMarker(string text)
        => text.Contains("plant:", StringComparison.OrdinalIgnoreCase)
        || text.Contains("plant -", StringComparison.OrdinalIgnoreCase)
        || NumberedAddressLabel.IsMatch(text);

    private static bool HasEnoughLetters(string text)
        => text.Count(char.IsLetter) >= 2;

    // A genuine manufacturer name never contains '@' (wrapped email
    // fragment), never contains a digit (wrapped street address/postal code
    // fragment), and always starts with an uppercase letter (rules out a
    // wrapped lowercase URL tail like "ons/" from a long website link
    // breaking across two lines). Confirmed necessary against real data:
    // without this, "lixg@nrec.com", "Vehmaistenkatu 5, 33730 Tampere,",
    // and a wrapped "ons/" URL fragment all falsely triggered new entries
    // in material 52.2 of the real Version B PDF, purely because a country
    // name happened to appear shortly after them for unrelated reasons.
    private static bool LooksLikeNameCandidate(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return false;
        if (trimmed.Contains('@')) return false;
        if (trimmed.Any(char.IsDigit)) return false;
        return char.IsUpper(trimmed[0]);
    }

    private static bool StartsWithAllCapsWord(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return false;
        var word = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        word = word.TrimEnd('.', ',');
        if (word.Length < 3) return false;
        return word.All(char.IsUpper) && word.All(c => char.IsLetter(c));
    }

    private static string? NullIfEmpty(string s)
    {
        var t = s.Trim('\n', '\r', ' ');
        return t.Length == 0 ? null : t;
    }

    private static bool IsHeaderBandLine(string text)
        => HeaderBandTokens.Any(tok => text.Contains(tok, StringComparison.OrdinalIgnoreCase));

    // Category names print in TWO styles in the real document, confirmed
    // directly: ALL-CAPS ("CONDUCTORS, BARE", "FIRE PROTECTION SYSTEM") and
    // Title Case ("HVAC Units", "Telecommunication Integration facilities").
    // The old all-caps-only check silently ignored every Title-Case one,
    // leaving currentCategory stuck on whichever ALL-CAPS category was last
    // seen - confirmed as the direct cause of "Telecommunication Integration
    // facilities" wrongly showing under "FIRE PROTECTION SYSTEM". Only an
    // uppercase FIRST letter is required now; the x0<100 left-margin check
    // at the call site (category headers only ever appear there) is what
    // keeps this from over-matching ordinary sentence-case content.
    private static bool LooksLikeCategoryHeader(string text)
    {
        var t = text.Trim();
        if (t.Length is < 3 or > 80) return false;
        if (t.Any(char.IsDigit) && !t.Contains("KV", StringComparison.OrdinalIgnoreCase)) return false;
        var letters = t.Where(char.IsLetter).ToList();
        if (letters.Count == 0) return false;
        return char.IsUpper(t[0]);
    }

    private static DateOnly? TryExtractHeaderDate(Page page)
    {
        var text = string.Join(" ", page.GetWords().Select(w => w.Text));
        var m = HeaderDateLine.Match(text);
        if (!m.Success) return null;
        if (int.TryParse(m.Groups[1].Value, out var d) &&
            int.TryParse(m.Groups[2].Value, out var mo) &&
            int.TryParse(m.Groups[3].Value, out var y))
        {
            try { return new DateOnly(y, mo, d); } catch { return null; }
        }
        return null;
    }

    private static List<List<Word>> GroupWordsIntoLines(Page page)
    {
        var words = page.GetWords().ToList();
        double TopFromPageTop(Word w) => page.Height - w.BoundingBox.Top;

        var sorted = words.OrderBy(TopFromPageTop).ThenBy(w => w.BoundingBox.Left).ToList();
        var lines = new List<List<Word>>();
        const double yTolerance = 4.0;

        foreach (var w in sorted)
        {
            var y = TopFromPageTop(w);
            var line = lines.LastOrDefault();
            if (line is not null && Math.Abs(TopFromPageTop(line[0]) - y) <= yTolerance)
            {
                line.Add(w);
            }
            else
            {
                lines.Add(new List<Word> { w });
            }
        }

        foreach (var line in lines) line.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
        return lines;
    }
}
