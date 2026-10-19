namespace Poe2Pricer.Core;

// Every threshold, measure and interval of the recognition and the overlay control in one place,
// each with why it stands where it stands. Pixel measures are for a 2560×1440 game window
// ("px at 1440"): a picture of another height is brought to that height before it is read
// (GameView), and what grows with the game's Popup Size is scaled by it (GameConfig.Scaled).
// Changing a value here changes the recognition: run "Poe2Pricer.Cli golden check" and the
// tests afterwards.
// "Measured" = set between the values of the right and the wrong cases on test pictures;
// "Dev-Jahn" = taken over from Dev-Jahn/poe2-ritual-checker (MIT) and not measured again here;
// "chosen" = no measurement behind it.
public static class Tuning
{
    // Recognizer: item symbols in the Favours grid.
    public static class Recognition
    {
        // Unsure below this score or this lead over the runner-up. Measured: every right match
        // scores at least .51 with a lead of at least .06; a greyed-out omen whose variants
        // differ in hue only can come out with no lead at all.
        public const double MinimumScore = .45;
        public const double MinimumMargin = .03;

        // A multi-cell item only with this score and lead. Measured: a real multi-cell unique
        // scores about .70 with a lead of .30; small items that happen to form a rectangle, or
        // tooltip edges, score about .50 with almost no lead.
        public const double MinimumMultiCellScore = .60;
        public const double MinimumMultiCellMargin = .10;

        // Learned prototypes (recognition-model.json) count only from this score and, against a
        // second learned item, with this lead; below the score a learned reference is ignored.
        // Dev-Jahn.
        public const double LearnedMinimumScore = .82;
        public const double LearnedMinimumLead = .04;

        // Score = gray correlation × GrayWeight + edge correlation × EdgeWeight − colour penalty.
        // Dev-Jahn.
        public const double GrayWeight = .70;
        public const double EdgeWeight = .30;

        // Colour penalty: only for coloured items (mean saturation above PenaltySaturation over
        // more than PenaltyMinimumPixels compared pixels), weight PenaltyWeight; pixels darker
        // than DarkSum (sum of B+G+R) or with a colour share below NoColour count as colourless.
        // Dev-Jahn.
        public const double PenaltyWeight = .45;
        public const double PenaltySaturation = .10;
        public const int PenaltyMinimumPixels = 12;
        public const float DarkSum = 100;
        public const float NoColour = .5f;

        // Reference art: pixels with alpha above this count. Dev-Jahn.
        public const int AlphaVisible = 80;

        // Extra references per omen and one-cell currency (scale, shift x, shift y as share of
        // the image): the game draws them slightly smaller or shifted. Dev-Jahn.
        public static readonly (double Scale, double ShiftX, double ShiftY)[] OmenVariants = [(1.0, 0.0, 0.0), (.96, -.025, 0.0), (.96, -.025, -.025), (.78, 0.0, 0.0)];
        public static readonly (double Scale, double ShiftX, double ShiftY)[] CurrencyVariants = [(1.0, 0.0, 0.0), (.78, 0.0, 0.0)];

        // Occupied cell: inner square (offset and size as share of the cell size), sampled every
        // second pixel; blue (normal), red (cannot wear) or bright content above these shares.
        // Blue: B > R × BlueOverRed + 4 and B > G × BlueOverGreen + 3; red: R > B × 1.3 + 4 and
        // R > G × 1.3 + 4; bright: brightest channel above BrightLevel. Dev-Jahn.
        public const double CellInsetX = .09;
        public const double CellInsetY = .08;
        public const double CellInner = .80;
        public const double BlueOverRed = 1.3;
        public const double BlueOverGreen = 1.15;
        public const double RedOverOthers = 1.3;
        public const int ColourLead = 4; // the "+ 4" above
        public const int BlueOverGreenLead = 3; // the "+ 3" above
        public const int BrightLevel = 65;
        public const double BlueShare = .14;
        public const double RedShare = .14;
        public const double BrightShare = .08;

        // Green ground: the item under the mouse (keyboard UI). G > R × 1.3 + 4 and
        // G > B × 1.3 + 4. Measured: the cells of the hovered item show .69–1.00, every other
        // cell .00.
        public const double GreenOverOthers = 1.3;
        public const double GreenShare = .14;
    }

    // Game resolution the pixel measures were taken at.
    public static class Screen
    {
        public const double ReferenceHeight = 1440;

        // FavoursLocator: a height within 2 % of 1440 is a cropped test capture, not a scaled
        // one. Chosen (screens run at standard resolutions).
        public const double SameScale = .02;

        // Stash tab, remnant list, rumour panel: other heights are refused. Since GameView
        // brings every picture to the reference height, this only guards a reader that is
        // handed a picture directly.
        public const int HeightTolerance = 30;

        // GameView. The game shows at most this aspect ratio and leaves black bars beyond it.
        // Measured on a window wider than that: the left-anchored windows (stash, remnant
        // list) move right by exactly the bar's width.
        public const double WidestAspect = 2.4;

        // A part of the picture is taken on whole steps of the scale (1080: 4 reference px,
        // 1200: 6, 1600: 9, 2160: 2). An odd window height gives steps too long to gain
        // anything; then the whole picture is taken. Chosen.
        public const int LongestStep = 64;

        // Around a part, this many of the game's pixels more are taken: resizing looks at the
        // neighbours (cubic: two on each side).
        public const int ResizeMargin = 3;
    }

    // FavoursLocator: the Favours window. px at 1440.
    public static class Favours
    {
        public const double Cell = 70.18;

        // Where the grid begins per UI mode: the window is the same in both, the controller UI
        // shows it 55 px lower. Measured at five window heights, vertically within ±1 px.
        // Dx, Dy: where the anchor lies from the grid's origin. In the controller UI the artwork
        // sits half a pixel off (the anchor fits one pixel further right and down, at .96
        // instead of 1.00); the origin stays where the cells were measured.
        public static readonly (UiMode Mode, int Top, int Dx, int Dy)[] GridTop = [(UiMode.KeyboardMouse, 356, 30, 705), (UiMode.Controller, 411, 31, 706)];

        // The anchor: the left end of the tribute bar's frame below the grid, looked for at its
        // height ± SearchRadius over the whole width. Measured: with the window open at least
        // .85 (defer mode; otherwise over .92, under tooltips and at every window height too),
        // on any other picture at most .53.
        public const string AnchorFile = "favours-tribute-bar.png";
        public const int SearchRadius = 16;
        public const double MinimumAnchorScore = .70;

        // Reroll button left of the tribute bar, relative to the grid origin (same in both
        // modes), and the number on it (RerollCount). A digit is a white area (every channel
        // ≥ DigitWhite, at least DigitPixels) whose border is black (every channel ≤ DigitBlack)
        // for at least DigitOutline of its length. Measured: a digit .69–.82, the die and the
        // arrows at most .24. The digits lie at the bottom left of the button (controller
        // 14×21 px, keyboard 7×12); their place is compared with NumberMargin px of margin
        // (RerollMark).
        public const int RerollLeft = 62;
        public const int RerollTop = -112;
        public const int RerollWidth = 88;
        public const int RerollHeight = 90;
        public const int DigitWhite = 200;
        public const int DigitBlack = 45;
        public const int DigitPixels = 8;
        public const double DigitOutline = .5;
        public const int NumberMargin = 3;

        // Cell memory (CellMemory): every cell is compared with the picture it was read from —
        // each pixel's brightness against the same pixel then, the median per cell. Pixels that
        // were darker than DarkPixel say nothing (a ratio of two small numbers); a cell needs
        // MeasurablePixels of the others. Measured: a free cell 1.00, under a tooltip .07–.27,
        // under the dialog about .30, greyed out or deferred .54–.81, under the mouse or the
        // controller cursor 1.00–1.84. Below CoveredBelow a cell is covered
        // (the tooltip darkens by a fixed factor, whatever lies behind it); above 1 / CoveredBelow
        // the stored picture was the covered one and the cell is free now.
        public const int DarkPixel = 12;
        public const int MeasurablePixels = 30;
        public const double CoveredBelow = .40;

        // A cell read without a usable result is read again from this brightness on: it may
        // have been covered when it was read and greyed out before it got free (.54 / .27 = 2,
        // just short of 1 / CoveredBelow). Too early costs one more read, nothing else.
        public const double DoubtfulBrighter = 1.5;

        // A place still shows what was stored of it when the picture's structure holds
        // (correlation of the grey values at least SameStructure): a cell of the grid, the
        // reroll button (RerollMark). Measured on picture pairs of the same roll: cells .96–1.00
        // — but frames around formerly deferred items, which come and go, press a cell down to
        // .55–.76. So the value only says whether a place looks as remembered, not whether a
        // new roll lies in the grid (that is the number on the reroll button, and after the
        // window was opened again the comparison of the items).
        public const double SameStructure = .80;

        // A window opened again shows the remembered roll when nothing in it contradicts that
        // and at least this many items are named for certain as remembered
        // (FavoursGameWindow.Agreement). Measured: the same roll agrees in every item without
        // a contradiction, another roll in a few items with many contradictions. The number
        // itself is chosen: more than one.
        public const int SameRollItems = 2;

        // A bought item leaves the empty cell: its picture differs from a stored empty cell by
        // at most EmptyDeviation grey levels (median). Measured: bought 0–3, occupied under a
        // tooltip 15–17.
        public const double EmptyDeviation = 6;

        // The window is still open without its button when at least StillThere of the stored
        // cells look as stored (defer mode: nearly all; a tooltip over half the grid: half).
        // Chosen; a closed window shows the scene, where no cell matches.
        public const double StillThere = .25;

        // A dialog dims the whole window: at least WindowDimmed of the stored cells are darker
        // than DimmedBelow at once. Measured: under the dialog every cell, mostly about .30 and
        // at most .55; a tooltip covers at most half the cells.
        public const double WindowDimmed = .9;
        public const double DimmedBelow = .7;

        // Where a price is drawn (LabelLayer): centred on the bottom edge of the item.
        public const int LabelSpotWidth = 56;
        public const int LabelSpotHeight = 28;

        // A tooltip's title bar is opaque: it does not darken what lies under it, it replaces
        // it. Structure of the label spot against the stored picture, measured: under the title
        // bar −.11 to .01; free at least .75, greyed out at least .39, deferred or selected in
        // defer mode at least .84, under the mouse at least .63, under the controller cursor at
        // least .60.
        public const double ReplacedBelow = .2;

        // Glance: a cell whose mean brightness moved by more than this against the frame last
        // followed means something happened in the grid. Measured: without a change at most
        // 0.2, the smallest real change 3.4 (a cell beside a tooltip), the mouse on an item
        // 11.5, the controller cursor 29–43.
        public const double CellMoved = 1;
    }

    // OmenGroups: greyed-out omens of a shape group ("!").
    public static class OmenGroups
    {
        // 75th percentile of the saturation inside the cell (without frame, quantity digit and
        // medallion). Measured: greyed-out shape-group omens 38–39, coloured ones at least 125.
        public const int GreySaturation = 80;
        public const double Percentile = .75;

        // Inner area of a 70-px cell (x, y, width, height), scaled with the cell: without frame,
        // quantity digit and medallion. The measures themselves: chosen.
        public const double CellWidth = 70.0;
        public const int InnerX = 12;
        public const int InnerY = 14;
        public const int InnerWidth = 38;
        public const int InnerHeight = 36;

        // Only pixels at least this bright count, and at least MinimumPixels of them. Chosen.
        public const int MinimumValue = 60;
        public const int MinimumPixels = 20;
    }

    // SelectionDetector: the controller cursor's caret "^" under the selected item.
    public static class Selection
    {
        // Caret template 40×22 px, its tip TipAbove px above the item's bottom edge.
        public const int TipAbove = 14;

        // Measured: .67–1.00 with the cursor in the grid, at most .54 otherwise.
        public const double Threshold = .60;

        // Search area beyond the grid (left/right, below) and how far the caret may sit from an
        // item's bottom centre. Chosen.
        public const int SearchSide = 10;
        public const int SearchBelow = 20;
        public const int MatchVertical = 8;
        public const int MatchHorizontal = 10;

        // Caret pixels: red ≥ 200 and green ≥ 185 (near white with a pale yellow rim).
        public const int MinimumRed = 200;
        public const int MinimumGreen = 185;
    }

    // RarityCorners: the ornament at two corners of a unique item in the Favours grid.
    public static class RarityCorner
    {
        public const string TopLeftFile = "rarity-unique-topleft.png";
        public const string BottomRightFile = "rarity-unique-bottomright.png";

        // The game draws the picture at this share of its size on the reference picture.
        // Measured: at 0.90 the ornament fits .95–.98, at 1.00 or 0.80 about .80.
        public const double Scale = .90;

        // Where the picture counts as there (of 255), and how far around the cell's corner
        // it is looked for (px).
        public const int AlphaVisible = 100;
        public const int Search = 3;

        // From this fit on the ornament is there: the middle of what lies between. Measured on
        // cells read as currency for certain: a plain currency cell at most .54 (the medallion
        // of a deferred item at the bottom right gives about .50), the corner of a large item
        // .79–.88. Nothing in between.
        public const double Minimum = .66;

        // What the catalog calls everything that is no unique.
        public const string CurrencyKind = "currency";
    }

    // TitleReader: tooltip titles by Windows OCR.
    public static class Titles
    {
        // Title bar height below the top of the left cap: one line (grey, magic), two lines
        // (rare, unique); cap width left and right. Measured.
        public const int OneLine = 46;
        public const int TwoLines = 76;
        public const int CapWidth = 40;

        // Smaller areas are not read; the title is enlarged before OCR. Chosen.
        public const int MinimumWidth = 20;
        public const int MinimumHeight = 10;
        public const double Enlarge = 2;

        // A title matches a catalog name at most this edit distance per name length (at least
        // 1); shorter OCR lines are ignored. Chosen.
        public const int DistancePerLength = 8;
        public const int MinimumKeyLength = 3;
    }

    // OccluderDetector: game popups over the Favours grid (tooltips, notifications, dialog, the
    // "ALT Inspect"/"SHIFT Compare" hints) and tooltips beside the remnant list. px at 1440.
    public static class Occluders
    {
        // Template thresholds. Measured: the ends of an item title bar fit at least .93 where
        // there is one, at most .57 elsewhere; the skill tooltip's piece at least .95, at most
        // .50 elsewhere; the close button of a notification and the key hints fit perfectly,
        // elsewhere at most .62. The dialog threshold is chosen.
        public const double CapThreshold = .75;

        // The game draws the title bar pieces of its art at this size times the popup size, on
        // a picture of 1440 px height. Measured: the pieces fit best at 0.89–0.91 times the
        // popup size, at every popup size and window height.
        public const double HeaderScale = .9;

        // The piece carries the bar's shadow: the bar itself begins this far inside it
        // (measured against the bar's frame line in the pictures).
        public const int HeaderInset = 1;

        // The left ends are searched at CoarseScale first, with CoarseSlack less than the
        // threshold, then exactly within CoarseMargin px of each candidate (LeftEnds).
        public const double CoarseScale = .5;
        public const double CoarseSlack = .15;
        public const int CoarseMargin = 3;
        public const double CloseThreshold = .70;
        public const double HintThreshold = .80;
        public const double DialogThreshold = .80;

        // Tooltip bottom border: a row below the title bar (at least MinimumTooltipHeight below
        // the top) whose median brightness is at least BrightBorder and 10th percentile at least
        // FullWidth, measured BorderInset px inside both sides. Measured: the tooltip body has
        // a median of at most 16, the bottom border at least 30 with a 10th percentile of 8–22,
        // separator lines inside the tooltip a 10th percentile of 1–3; the title bar reaches
        // about 75 px down. BorderInset: chosen.
        public const int BrightBorder = 30;
        public const int FullWidth = 6;

        // Pure black pixels of a row do not count (no border is drawn over a black screen), but
        // the row must show on at least this share of the width. Measured: a border over black
        // beside the window shows on .77–.92 of the width, otherwise on .96–1.00.
        public const double VisibleBorder = .5;
        public const int MinimumTooltipHeight = 80;
        public const int BorderInset = 20;

        // Below the real border the screen is no longer darkened: the band below the row at
        // least UndimmedFactor × as bright as the band above it (at least UndimmedFloor). A
        // bright overlay seen through the tooltip (an FPS graph) stays dark on both sides; that
        // case is why the test exists. Factor, bands and floor: chosen.
        public const int UndimmedAboveFrom = 10;
        public const int UndimmedAboveTo = 3;
        public const int UndimmedBelowFrom = 4;
        public const int UndimmedBelowTo = 11;
        public const double UndimmedFactor = 2;
        public const double UndimmedFloor = 4;

        // Right end: the mirrored cap searched from RightFrom px right of the left cap, in a band
        // from RightAbove px above, RightExtra px taller than the cap. Chosen.
        public const int RightFrom = 120;
        public const int RightAbove = 4;
        public const int RightExtra = 8;

        // Game notification: from its close button about 720 px to the left and 60 px up and
        // down (measured on one notification; the width depends on the text), with some margin.
        public const int NotificationLeft = 780;
        public const int NotificationHalfHeight = 75;

        // Keyboard hints next to the hovered item: each line ~118×26 px from 4 px left of its
        // key; the first HintLeft px may lie on the hovered item itself (its recognition holds).
        // Measured.
        public const int HintLeft = 10;
        public const int HintRight = 114;
        public const int HintTop = 5;
        public const int HintBottom = 21;

        // Confirmation dialog: from the CANCEL button about 440 px left, 140 px up, 60 px right,
        // 40 px down (measured), with a generous margin to the right and below.
        // DialogCap: the ends of the button that carry no lettering (DialogButton).
        public const int DialogCap = 16;
        public const int DialogLeft = 460;
        public const int DialogUp = 160;
        public const int DialogRight = 230;
        public const int DialogDown = 90;

        // Tooltips beside the remnant list: fixed width (labels sit at the left edge); the bottom
        // is where the dimming of the scene ends (OccluderDetector.DimmingEnd). Looked at: from
        // DimFrom px inside the left frame to
        // BesideWidth, from DimBelowTitle px below the title bar, columns averaged in groups of
        // DimBin. A step: below at least DimFloor bright and DimFactor × as bright as above
        // (the dimming itself makes 4 to 5), each the median of DimWindow rows (a line of text
        // is lower); it counts when DimShare of the columns show it. DimFine: the rows of the
        // short window that places it. DimFrom, DimBelowTitle, DimBin: chosen.
        public const int BesideWidth = 400;
        public const int DimFrom = 20;
        public const int DimBelowTitle = 4;
        public const int DimBin = 4;
        public const int DimWindow = 24;
        public const int DimFine = 6;
        public const int DimFloor = 20;
        public const double DimFactor = 2.5;
        public const double DimShare = 2.0 / 3;

        // Tooltips over a stash tab: the bottom where the near-black inner margin
        // (MarginFrom..MarginTo px inside the left frame, from below the title bar) is brighter
        // than MarginBright for MarginRows rows (a border-line estimate stops at the separator
        // lines of short tooltips). The values: chosen. The title bar is as high as its piece
        // of art (OccluderDetector.LeftEnds), one line or two.
        public const int MarginFrom = 7;
        public const int MarginTo = 25;
        public const double MarginBright = 40;
        public const int MarginRows = 3;

        // Height of a title bar from the top of its end cap, for a tooltip whose inner margin
        // cannot be measured (OccluderDetector.TooltipsOver). Measured: one line 45 px, two
        // lines 78 px. The higher holds for all: what lies this close below the top is always
        // tooltip.
        public const int TitleHeight = 80;

        // Hidden items (Recognition.WithoutOccluded): overlap of at least HideOverlap px with a
        // popup; an unsure item without shape group also within HideNeighbour px of one (the
        // uncovered rest of a partly covered unique).
        public const int HideOverlap = 10;
        public const int HideNeighbour = 6;
    }

    // StashTabReader: special stash tabs by their art, filled and covered slots. px at 1440.
    public static class Stash
    {
        // Tab found / still there: mean of the best BestTiles share of 5×5 tile correlations on fine
        // structure (high-pass, quarter size). Measured: the tab itself .99–1.00 in both UI
        // modes, under a tooltip too; other tabs and windows at most .28, a sub-tab of the same
        // tab .45 (told apart by the sub-tab button); the tab fading out still gives about .5.
        public const double MinimumScore = .75;
        public const double KeepScore = .75;

        // Of the tiles the best BestTiles share counts: a large tooltip in the controller UI
        // covers more than half of them, and on a resized picture the free tiles fit less well,
        // so the better half would lose the tab. Measured: with the tab at least .83, without
        // at most .36.
        public const double BestTiles = .4;

        // StashTabReader.Placements: the width the controller offset was measured at, and how
        // much further right the tab stands on a narrower picture.
        public const int ReferenceWidth = 2560;
        public const int ControllerNarrower = 5;
        public const int Shrink = 4;
        public const int TilesAcross = 5;
        public const int MinimumTilePixels = 30;
        public const double Blur = 2;

        // Slots are left out of the tab template with this margin around them. Chosen.
        public const int SlotMaskMargin = 12;

        // Sub-tab button glow (red minus blue), measured: active 89–137, inactive 33–54, under
        // a tooltip at most 19; then the whole area incl. slot frames: own sub-tab .98, the
        // next .25.
        public const double MinimumGlowLead = 25;
        public const double MinimumLitGlow = 70;
        public const double MinimumFullScore = .60;

        // Filled slot: item ground (ItemGround: blue, red, green under the mouse — the same
        // rule as in the Favours grid) in a Ring-px ring just inside the frame (inset SlotInset
        // px). Measured: filled at least .145 (under the controller's selection frame), empty
        // without violet ground at most .04; the violet rune rows (.38 even when empty) are
        // told apart by the brightness below. SlotInset: chosen.
        public const double MinimumBlue = .05;
        public const int Ring = 6;
        public const int SlotInset = 5;

        // Covered: the lower side of that ring (where the label goes) against the usual ground
        // brightness of the tab's filled slots. Two readings: cut into LabelParts parts, the
        // darkest part's median below CoveredBelow (a popup on a part of the strip); or the
        // darkest quarter of all its pixels below CoveredQuarterBelow (a popup with bright text
        // across the strip). Measured at five window heights: the ground is equally bright
        // everywhere. Darkest third: free at least .47, under a tooltip at most .19 — but
        // .40–.60 under the help text, where bright writing runs across the strip. Darkest
        // quarter: free at least .50, under tooltip and help text at most .31.
        // Not dark and so not caught: the title bar of a tooltip and the key hint — both are
        // found by their looks (StashGameWindow).
        public const double CoveredBelow = .35;
        public const double CoveredQuarterBelow = .4;
        public const int LabelParts = 3;

        // Where a slot's label is drawn (LabelLayer: centred on the lower edge, inside the
        // slot), for the key hints that may lie on it.
        public const int LabelSpotWidth = 56;
        public const int LabelSpotHeight = 31;

        // And bright art: 95th percentile of the brightest channel, measured filled at least
        // 147, empty at most 115 (violet rune rows pass the blue test).
        public const int MinimumBright = 130;
        public const int BrightPercentileShare = 20; // top 1/20 = 95th percentile

    }

    // StashIdentifier: naming stash slots against the poe.ninja item art.
    public static class StashNames
    {
        // Measured: a right name scores at least .46; only gross errors are rejected, as among
        // the essences of one tier the wrong art can lead by up to .15.
        public const double RejectBelow = .25;
        public const double AcceptScore = .50;
        public const double AcceptLead = .20;

        // Art sizes as share of the slot (best of them), ±Shift px; Size is the main size.
        // Score = correlation − ColourWeight × mean colour difference. Chosen.
        public static readonly double[] Sizes = [.84, .9, .96];
        public const double Size = .9;
        public const int Shift = 3;
        public const double ColourWeight = 2;

        // Preselection on 12×12 thumbnails before the full comparison.
        public const int PreselectCount = 24;

        // Art pixels with alpha above this count for the colour; the art is laid on the blue cell
        // ground as the game draws it (RGB 14, 14, 55).
        public const int AlphaVisible = 160;
        public static readonly OpenCvSharp.Vec3b Ground = new(55, 14, 14);
    }

    // RemnantReader: the remnant reward list ("Runeshape Combinations"). px at 1440.
    public static class Remnants
    {
        // The anchor: the upper right corner of the book's page (the same artwork in both UI
        // modes), where it lies per mode, the list area and the window's right edge at that
        // place. The controller UI draws rows twice as high and scrolls after about six and a
        // half rows. Looked for at its height ± SearchRadius over the whole width. Measured:
        // with the list open at least .94 (every window height, both modes), on any other
        // picture at most .37. The title's lettering is no anchor: the controller UI sets it
        // larger at small window heights.
        public const string AnchorFile = "runeshape-page.png";
        public static readonly (UiMode Mode, int X, int Y, Box List, int Right)[] Layouts =
        [
            (UiMode.Controller, 560, 280, new Box(70, 345, 670, 745), 772),
            (UiMode.KeyboardMouse, 563, 128, new Box(70, 190, 670, 650), 776),
        ];
        public const int SearchRadius = 16;
        public const double MinimumAnchorScore = .65;

        // A reward's name is written right-aligned: its line ends this close to the list's
        // right edge. Measured: reward lines end 16–29 px before the edge, what the OCR makes
        // of the rune symbols ("eeeü", "auazfi") ends 400 px and more before it.
        public const int RightAligned = 80;

        // A row is known again by its picture (RowPicture): the strip RowBand px above and
        // below the centre of its name, compared up to RowShift px up or down. Measured on a
        // scrolled list: the same row at another place .92–1.00 (.92 under the mouse),
        // different rows at most .73 ("Skill Level 20: Verisium Manifestations" against
        // "Skill: Verisium Manifestations"). A taller strip takes in the selection frame of the
        // controller UI and drops the selected row to .83.
        public const int RowBand = 16;
        public const int RowShift = 4;
        public const double SameRow = .85;

        // A view belongs to the list known so far when at least MinimumOverlap of its rows lie
        // on the same rows there and at most MaximumMisread on another (RemnantList). Chosen.
        public const int MinimumOverlap = 2;
        public const int MaximumMisread = 1;

        // Tooltips beside the list are searched in this band around the window's right edge:
        // the game opens them that much further right at a larger popup size. Chosen.
        public const int TooltipBandLeft = 20;
        public const int TooltipBandRight = 60;

        // List changed (scrolled, new remnant): mean difference of a 16×48 gray picture above
        // ChangedAbove. Chosen.
        public const int SignatureWidth = 16;
        public const int SignatureHeight = 48;
        public const double ChangedAbove = 4;
    }

    // RumourReader and RumourBook: the island rumour panel. px at 1440.
    // IslandMarks: which island the rumour panel stands at. px at 1440.
    public static class Islands
    {
        // The ship marker is searched this far beside the panel and above / below its lower
        // edge. Measured: below the panel it stands 102 px under the lower edge, beside it
        // 80 px from the panel's edge and anywhere along the panel's height (093329: 229 px
        // above the lower edge).
        public const int MarkerBeside = 240;
        public const int MarkerAbove = 80;
        public const int MarkerBelow = 240;

        // The sails: a 44 × 40 white blob; around them the dark square and its frame, 64 × 64
        // in all. Sails white from SailWhite (of 255) and greyer than SailGrey (saturation of
        // 255), the square darker than SquareDark. Measured over 28 pictures: the sails fill
        // .40–.48 of the inner box, the ring is .60–.80 dark; with the controller's gold frame
        // on the marker still .60. Nothing else near the panel fills .35 of the inner box with
        // white and has a dark ring: foliage and surf have no dark ring, waypoint glows are
        // coloured.
        public const int MarkerInner = 44;
        public const int MarkerOuter = 64;
        public const int SailWhite = 200;
        public const int SailGrey = 77;
        public const int SquareDark = 70;
        public const double SailsAtLeast = .35;
        public const double SailsAtMost = .85;
        public const double RingDarkAtLeast = .45;
        public const double RingWhiteAtMost = .2;

        // Where nothing is read: the game's bars at the edges (both UIs, generously) and the
        // panel with a margin. Chosen.
        public const int HudTop = 140;
        public const int HudBottom = 1060;
        public const int HudLeft = 140;
        public const int HudRight = 160;
        public const int PanelMargin = 20;
        public const int PanelMarginBelow = 40;

        // Route dots: yellow (OpenCV hue, half degrees), saturated, bright; 6–70 px, at most
        // DotSize wide and high, filled DotFill of their box. They are dashes, not dots: about
        // 8 × 3 px, so a slanted one fills only a quarter of its box — with DotFill .45 and
        // DotSize 10 (until 19.10.) only the level ones were found, 30–50 of about 200 in a
        // picture, and the prints were thin and unstable. Measured: hue 40–60°, saturation
        // .35–.53, value .72–1.0. Foliage glints: hue 49–76°, saturation .33–.57, value up to
        // .98 — not told apart by colour, hence the ring: the ring RingFrom–RingTo px around a
        // dot holds less than RingForeign of green (LeafHue…, the island) or yellow (the dot is
        // part of a glint). A dot has another dot DotSpacingFrom–DotSpacingTo px away.
        public const int DotHueFrom = 19;
        public const int DotHueTo = 35;
        public const int DotSaturation = 89;
        public const int DotValue = 153;
        public const int DotAreaFrom = 6;
        public const int DotAreaTo = 70;
        public const int DotSize = 14;
        public const double DotFill = .25;
        public const int LeafHueFrom = 31;
        public const int LeafHueTo = 75;
        public const int LeafSaturation = 77;
        public const int LeafValue = 64;
        public const int RingFrom = 6;
        public const int RingTo = 11;
        public const double RingForeign = .2;
        public const int DotSpacingFrom = 8;
        public const int DotSpacingTo = 20;

        // Waypoint glows: saturation above GlowSaturation, value above GlowValue (of 255; the
        // water is dark: value about .18), 60–2500 px, roundish. Measured.
        public const int GlowSaturation = 102;
        public const int GlowValue = 140;
        public const int GlowAreaFrom = 60;
        public const int GlowAreaTo = 2500;
        public const int GlowSizeAtLeast = 10;
        public const double GlowAspectFrom = .5;
        public const double GlowAspectTo = 2;
        public const double GlowFill = .35;

        // Matching: the scale between two openings is searched from ScaleFrom to ScaleTo in
        // ScaleSteps steps (3 % apart); a point hits when a point of the other print lies within
        // Reach px (at the other's scale). The dashes move along their routes (the game
        // animates them), so two pictures of the same view never hold the same dashes; Reach
        // covers that. Measured 19.10. on 36 pictures of five islands, both UIs, with the
        // dashes found as dashes (DotFill .25): the same island at the same place scores .70
        // or more (both UIs, 200 dashes a picture at the controller), another island at most
        // .18 with 30 points or more in common (.33 with 27: the floor is MinimumEvidence).
        // The same island after dragging and zooming (keyboard) .5 or more; the same island
        // seen with the other UI (another zoom, another view) .13–.31 — a new collection, as
        // after dragging (README, known limits). Before 19.10., with the thin prints, the same
        // view went down to .27 and the book opened one island three times.
        public const double ScaleFrom = .6;
        public const double ScaleTo = 1.6;
        public const int ScaleSteps = 41;
        public const double Reach = 10;

        // IslandBook: the same island from SameIsland on (measured .70 against .18, see Reach),
        // judged only with MinimumEvidence points in common; without a judgement the panel
        // standing within SamePanelPlace px of where it stood means the island seen last.
        // At most MostIslands are kept (a guard, not a measure: a match costs milliseconds).
        public const double SameIsland = .45;
        public const int MinimumEvidence = 30;
        public const int SamePanelPlace = 3;
        public const int MostIslands = 100;

        // Island.ProbablyComplete: the chance that one more rumour exists and never showed, at
        // most this. Three seen: from the third roll without a new one (1.6 %); four: the
        // fourth (2.6 %); five: the fifth (3.1 %); six: the sixth (3.5 %). Chosen.
        public const double HiddenRumourAtMost = .05;
    }

    public static class Rumours
    {
        // The two ends of the title bar as a pair (RumourReader.Locate): the left piece is
        // searched at half size, its FrameCandidates best places above Threshold − CoarseSlack
        // are looked at exactly, each with the right piece where it belongs (FrameBetween px
        // further right, FrameSlack px either way). Measured: with the panel .96–1.00, without
        // at most .49 — the threshold lies in the middle.
        public const double Threshold = .75;
        public const double CoarseSlack = .25;
        public const double CoarseScale = .5;
        public const int FrameCandidates = 6;
        public const int FrameBetween = 444;
        public const int FrameSlack = 3;
        public const int SamePlace = 1;
        public const int FineMargin = 8;

        // The title (where "Uncharted Waters" stands, 360 × 39) against the corner of the left
        // piece: the panel's measures below count from the title, as they did when it was
        // found by its lettering.
        public const int TitleWidth = 360;
        public const int TitleHeight = 39;
        public const double FrameToTitleX = 62 + TitleWidth / 2.0;
        public const double FrameToTitleY = 4 + TitleHeight / 2.0;

        // From the title (360×39): rows between "Island Rumours" and "Requires:", the frame
        // about 70 px left and right. Measured.
        public const int RowsLeft = 50;
        public const int RowsDown = 160;
        public const int RowsHeight = 150;
        // How far the panel may move between two glances and still be followed instead of
        // read anew: in the controller UI it flies in and comes to rest within about 100 px.
        // The log notes every move.
        public const int TrackMargin = 60;

        public const int PanelLeft = 70;
        public const int PanelUp = 15;
        public const int PanelHeight = 470;

        // Matching a read line against the table: letters only, at least MinimumKeyLength;
        // similarity (longest common subsequence) at least MinimumSimilarity with MinimumLead
        // over the runner-up. Measured: a reading that names the right rumour reaches at least
        // .62 with a lead of .2 — when it names one at all, the handwriting is beyond the OCR
        // often enough (hence the line pictures, RumourShapes).
        public const int MinimumKeyLength = 4;
        public const double MinimumSimilarity = .5;
        public const double MinimumLead = .1;

        // The ways the panel is read (enlargement, black and white), in this order; a line
        // (centre within SameLine px; the lines are ~50 px apart, readings of one line differ
        // by at most 2 px) keeps the first reading that names a rumour. Measured: each single
        // reading leaves some lines unnamed and names none wrongly; these three together name
        // nearly all. The remaining readings follow only while a line is still unnamed
        // (MoreReadings): a line the first three garble can come out right in one of them.
        public static readonly (double Scale, bool Binarize)[] Readings = [(2, false), (2, true), (3, true)];
        public static readonly (double Scale, bool Binarize)[] MoreReadings = [(1, false), (1, true), (1.5, true), (1.5, false), (2.5, false), (2.5, true), (3, false), (4, false), (4, true)];
        public const int SameLine = 20;

        // Line pictures (RumourShapes): the parchment without its frame (InkInset px left and
        // right, InkHeight px down), ink darker than InkBelow × the median brightness; a line
        // ends after LineGap rows without ink and is at least this high and wide. Two pictures
        // show the same line from SameShape overlap on, ShapeLead ahead of any other rumour,
        // at the best of ±ShapeShift px. Measured: the same line .86–1.00, different lines
        // .14–.27.
        public const int InkInset = 60;
        public const int InkHeight = 146;
        public const double InkBelow = .5;
        public const int LineGap = 4;
        public const int MinimumLineHeight = 12;
        public const int MinimumLineWidth = 30;
        // Against a line drawn from the font (RumourLineArt) the overlap is lower than
        // against a picture from the game, because the game draws a little thinner: the right
        // line .62–.76, the best wrong one .31. SameShape and ShapeLead hold for both kinds; a
        // picture from the game counts as already stored from SamePicture on (game against game
        // at least .86, drawn against game at most .76).
        public const double SameShape = .45;
        public const double ShapeLead = .15;
        public const double SamePicture = .8;
        public const int ShapeShift = 2;

        // Drawing a line: font size in px per em at 1440 px screen height, ink where the
        // drawing is darker than DrawnInk (of 255), a margin around the text. Measured: size 32
        // and threshold 128 match the game's lines best of the sizes and thresholds around them.
        public const double DrawnSize = 32;

        // The texts are drawn at these sizes besides, for pictures of other window heights
        // (RumourShapes.SetDrawn).
        public static readonly double[] DrawnOtherSizes = [.94, .97, 1.03, 1.06];

        // The German client writes the lines in Caveat Bold, set by the font's own rules
        // (RumourLineArt). Measured: at size 27 the right line overlaps .56–.77 (a wrong one at
        // most .30), a quarter pixel beside it already .1–.2 less. Each line has its maximum at
        // a slightly different size (it depends on the text; why is unknown); with the three
        // sizes here every line reaches at least .65. The first is the usual size; all three
        // are drawn at the other window heights too (DrawnOtherSizes).
        public static readonly double[] CaveatSizes = [27, 26.75, 27.375];
        public const int DrawnInk = 128;
        public const int DrawnMargin = 8;

        // A line's picture is remembered only when the OCR named it this clearly (what a right
        // reading reaches, see MinimumSimilarity); a weaker match is shown, not remembered.
        public const double LearnSimilarity = .62;
        public const double LearnLead = .2;

        // Other rumours on the panel: mean difference of a 32×12 gray picture above ChangedAbove.
        // Chosen.
        public const int SignatureWidth = 32;
        public const int SignatureHeight = 12;
        public const double ChangedAbove = 4;
    }

    // Labels and PriceBook: how prices are shown.
    public static class Labels
    {
        // Colour tiers in divine, so they follow the exchange rate (like Dev-Jahn's colour
        // scale): below Low grey, below Normal white, below DivineFrom yellow, from DivineFrom
        // orange — also where the unit switches to div.
        public const decimal Low = .02m;
        public const decimal Normal = .2m;
        public const decimal DivineFrom = 1m;

        // Fewer than this many offers behind a unique price: shown with "~".
        public const int ThinBelow = 5;

        // Remnant rows: plate size; controller inside the row, RowInset px from the list's right
        // edge and ControllerRuneOffset px above the name (the runes stand in the upper half of
        // the row); keyboard RowGap px beside the window, where there is room. px at 1440;
        // plate size, RowInset, RowGap: chosen.
        public const int RowLabelWidth = 110;
        public const int RowLabelHeight = 34;
        public const int RowInset = 12;
        public const int RowGap = 8;

        // The arrow to the best reward out of view stands this far above or below the list,
        // at its left edge: left of the title and below the last row nothing else is drawn
        // (in the controller UI the labels of the rows stand at the right).
        public const int ArrowGap = 2;
        public const int ControllerRuneOffset = 52;

        // A remnant row name read by OCR matches a poe.ninja name within 1 edit per
        // EditsPerLetters letters (at least 1), from MinimumKeyLength letters on. The OCR of
        // the list is clean (a count "1x" read as "lx" at most).
        public const int EditsPerLetters = 8;
        public const int MinimumKeyLength = 3;
    }

    // Overlay control and data sources: intervals and time limits.
    public static class Control
    {
        // Glance (still there?) and follow (changed?) while shown. The glance decides how long
        // prices stand in the air after the window closed: it takes only the window's part of
        // the picture (a few ms), so it can come every 50 ms, and the prices go as fast as they
        // came.
        public static readonly TimeSpan GlanceEvery = TimeSpan.FromMilliseconds(50);
        public static readonly TimeSpan FollowEvery = TimeSpan.FromSeconds(1);

        // Automatic scan at most this often, whatever the settings say. A search with no window
        // open takes about 45 ms for the picture and 65 ms for the four windows, so this is
        // roughly half of one processor core while the game is in front — the price of prices
        // that appear as soon as a window opens.
        public const double AutoMinimumSeconds = .2;

        // No sense in a longer pause between two looks, and a timer takes no more than some 49
        // days: a huge value in the settings must not end the program at the start.
        public const double AutoMaximumSeconds = 3600;

        // The game window is looked for this often (the game may start or restart). Chosen.
        public static readonly TimeSpan GameSearchEvery = TimeSpan.FromSeconds(5);

        // A failure that comes again and again is logged once in this time.
        public static readonly TimeSpan SameErrorEvery = TimeSpan.FromMinutes(5);

        // poe.ninja: a category older than MaxAge is asked again (an unchanged one costs a 304),
        // requests Spacing apart; poe.ninja and the item art time out after RequestTimeout.
        // After a load that failed, the next one is not started before RetryPause.
        public static readonly TimeSpan PricesMaxAge = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan PricesRetryPause = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan RequestSpacing = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    }
}
