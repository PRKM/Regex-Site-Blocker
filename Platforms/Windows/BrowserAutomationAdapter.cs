using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.EventHandlers;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Regex_Site_Blocker_App.Services.BrowserBlocking;

namespace Regex_Site_Blocker_App.WinUI
{
    internal sealed class BrowserAutomationAdapter : IDisposable
    {
        private readonly UIA3Automation automation = new();
        private readonly List<EventHandlerBase> subscriptions = [];
        private readonly Action wake;
        // UIA_FullDescriptionPropertyId; FlaUI 5 does not expose this on Element.Properties.
        private static readonly FlaUI.Core.Identifiers.PropertyId fullDescription =
            FlaUI.Core.Identifiers.PropertyId.Register(FlaUI.Core.AutomationType.UIA3, 30159, "FullDescription");
        private CaptureResult? lastCapture;
        private CaptureResult? cachedCapture;
        private long structureRevision;
        private long capturedRevision;
        private long discoveredAt;
        private sealed record CaptureResult(BrowserObservation Snapshot, string Browser, AutomationElement Address,
            string AddressKey, string AddressId,
            AutomationElement? Selected, AutomationElement? Document,
            List<AutomationElement> NavigationButtons, List<AutomationElement> Elements,
            List<AutomationElement> StructureParents);
        public BrowserAutomationAdapter(Action wake)
        {
            this.wake = wake;
            // Keep provider calls finite when a browser is unresponsive.
            automation.ConnectionTimeout = TimeSpan.FromSeconds(2);
            automation.TransactionTimeout = TimeSpan.FromSeconds(2);
        }

        public BrowserObservation? Read()
        {
            lastCapture = null;
            long revision = Interlocked.Read(ref structureRevision);
            CaptureResult? capture = null;
            bool reused = false;
            if (cachedCapture != null && capturedRevision == revision &&
                Stopwatch.GetElapsedTime(discoveredAt) < (cachedCapture.Document == null ?
                    TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(2)))
            {
                try { capture = Refresh(cachedCapture); reused = capture != null; }
                catch (Exception ex) { Debug.WriteLine($"UIA cached elements expired: {ex.Message}"); }
            }
            capture ??= Capture();
            if (capture == null)
            {
                ClearSubscriptions(); return null;
            }
            var snapshot = capture.Snapshot;
            var elements = capture.Elements;
            if (!reused)
            {
                ClearSubscriptions();
                foreach (var element in elements)
                {
                    try
                    {
                        subscriptions.Add(element.RegisterPropertyChangedEvent(TreeScope.Element, (_, _, _) => wake(),
                            automation.PropertyLibrary.Element.Name, automation.PropertyLibrary.Element.IsOffscreen,
                            automation.PropertyLibrary.Element.HelpText,
                            fullDescription,
                            automation.PropertyLibrary.Element.IsEnabled, automation.PropertyLibrary.Value.Value,
                            automation.PropertyLibrary.SelectionItem.IsSelected,
                            automation.PropertyLibrary.LegacyIAccessible.State,
                            automation.PropertyLibrary.LegacyIAccessible.Name,
                            automation.PropertyLibrary.LegacyIAccessible.Description));
                    }
                    catch (Exception ex) { Debug.WriteLine($"UIA subscription: {ex.Message}"); }
                }
                foreach (var parent in capture.StructureParents)
                {
                    try
                    {
                        // Watch the chrome/document containers, not changes inside website controls.
                        subscriptions.Add(parent.RegisterStructureChangedEvent(TreeScope.Element, (_, _, _) =>
                        {
                            Interlocked.Increment(ref structureRevision);
                            wake();
                        }));
                    }
                    catch (Exception ex) { Debug.WriteLine($"UIA structure subscription: {ex.Message}"); }
                }
                // Bounded recovery for browsers/providers that omit structure notifications.
                discoveredAt = Stopwatch.GetTimestamp();
            }
            cachedCapture = capture;
            capturedRevision = revision;
            lastCapture = capture;
            return snapshot;
        }

        private static CaptureResult? Refresh(CaptureResult capture)
        {
            // Values are always current; only element references are retained between reads.
            if (!SameTarget(capture) || capture.Document != null && Visible(capture.Document) != true) return null;
            string? addressUrl = AbsoluteAddress(Value(capture.Address));
            bool focused = capture.Address.Properties.HasKeyboardFocus.ValueOrDefault;
            string signal = LoadingSignal(capture.Selected, capture.Document, capture.NavigationButtons);
            // A value/loading change alone does not require rebuilding the tree. Read it immediately;
            // structural notifications and element validity checks handle actual replacements.
            return capture with { Snapshot = new BrowserObservation(capture.Snapshot.Window, capture.Snapshot.Tab,
                capture.Document == null ? null : Value(capture.Document), addressUrl, focused, IsLoading(signal)) };
        }

        private CaptureResult? Capture()
        {
            nint hwnd = GetForegroundWindow();
            if (hwnd == 0 || IsIconic(hwnd)) return null;
            GetWindowThreadProcessId(hwnd, out uint pid);
            using var process = Process.GetProcessById((int)pid);
            string browser = process.ProcessName.ToLowerInvariant();
            if (browser is not ("chrome" or "msedge" or "firefox" or "whale" or "brave" or "opera" or "vivaldi")) return null;
            var window = automation.FromHandle(hwnd);
            var nodes = ChromeElements(window);
            var address = nodes.FirstOrDefault(element => IsAddress(element, browser));
            if (address == null)
            {
                Debug.WriteLine($"UIA {browser}: address element unavailable (nodes={nodes.Count}).");
                return null;
            }
            var selected = nodes.FirstOrDefault(element => element.ControlType == ControlType.TabItem &&
                element.Patterns.SelectionItem.TryGetPattern(out var pattern) && pattern.IsSelected.Value);
            var documents = nodes.Where(element => element.ControlType == ControlType.Document && Visible(element) == true).ToArray();
            // Retain the visible document even before it exposes an HTTP URL, so its Busy state
            // and later URL updates remain observable during the initial connection phase.
            var document = documents.FirstOrDefault(element => BrowserBlockPolicy.TryHost(Value(element), out _)) ?? documents.FirstOrDefault();
            var navigation = nodes.Where(element => element.ControlType == ControlType.Button && IsNavigationButton(element)).ToList();
            string signal = LoadingSignal(selected, document, navigation);
            string tab = selected != null ? Key(selected) : document != null ? Key(document) : "unknown";
            var snapshot = new BrowserObservation(hwnd, tab, document == null ? null : Value(document),
                AbsoluteAddress(Value(address)), address.Properties.HasKeyboardFocus.ValueOrDefault, IsLoading(signal));
            var watched = new List<AutomationElement> { address };
            if (selected != null) watched.Add(selected);
            if (document != null) watched.Add(document);
            watched.AddRange(navigation);
            var parents = new List<AutomationElement>();
            foreach (var element in watched)
            {
                try
                {
                    var parent = element.Parent;
                    if (parent != null && !parents.Any(existing => Key(existing) == Key(parent))) parents.Add(parent);
                }
                catch (Exception ex) { Debug.WriteLine($"UIA structure container unavailable: {ex.Message}"); }
            }
            return new(snapshot, browser, address, Key(address), address.Properties.AutomationId.ValueOrDefault ?? "",
                selected, document, navigation, watched, parents);
        }

        private static List<AutomationElement> ChromeElements(AutomationElement window)
        {
            // Do not descend into website controls: avoids mistaking a page's search field for the address bar.
            var nodes = new List<AutomationElement>();
            var queue = new Queue<(AutomationElement Node, int Depth)>(); queue.Enqueue((window, 0));
            while (queue.TryDequeue(out var item) && nodes.Count < 512)
            {
                nodes.Add(item.Node);
                if (item.Depth >= 10 || item.Node.ControlType == ControlType.Document) continue;
                foreach (var child in item.Node.FindAllChildren()) queue.Enqueue((child, item.Depth + 1));
            }
            return nodes;
        }

        private static bool IsAddress(AutomationElement element, string browser)
        {
            string id = Optional(() => element.Properties.AutomationId.ValueOrDefault, "") ?? "";
            string name = Optional(() => element.Properties.Name.ValueOrDefault, "") ?? "";
            return BrowserElementRules.IsAddress(browser, element.ControlType.ToString(), id, name);
        }
        private static bool IsStop(AutomationElement element)
            => StopSignal(element) != "NONE";

        private static string StopSignal(AutomationElement element)
        {
            string id = Optional(() => element.Properties.AutomationId.ValueOrDefault, "") ?? "";
            string? name = Optional(() => element.Properties.Name.ValueOrDefault, (string?)null);
            if (BrowserElementRules.IsStop(id, name)) return "STOP_BUTTON";
            string? description = Optional(() =>
                element.FrameworkAutomationElement.TryGetPropertyValue<string>(fullDescription, out var text) ? text : null,
                (string?)null);
            if (BrowserElementRules.IsStop("", null, fullDescription: description)) return "STOP_BUTTON_FULL_DESCRIPTION";
            // Chromium can keep the accessible Name as Reload while changing its tooltip/description.
            string? help = Optional(() => element.Properties.HelpText.ValueOrDefault, (string?)null);
            if (BrowserElementRules.IsStop("", null, helpText: help)) return "STOP_BUTTON_HELP_TEXT";
            return Optional(() =>
            {
                if (!element.Patterns.LegacyIAccessible.TryGetPattern(out var legacy)) return "NONE";
                if (BrowserElementRules.IsStop("", null, legacy.Name.ValueOrDefault)) return "STOP_BUTTON_LEGACY_NAME";
                return BrowserElementRules.IsStop("", null, legacyDescription: legacy.Description.ValueOrDefault)
                    ? "STOP_BUTTON_LEGACY_DESCRIPTION" : "NONE";
            }, "NONE");
        }
        private static string LoadingSignal(AutomationElement? selected, AutomationElement? document, List<AutomationElement> navigation)
        {
            if (selected != null && Busy(selected)) return "SELECTED_TAB_BUSY";
            bool unavailable = false;
            if (document != null)
            {
                bool? visible = Visible(document);
                unavailable |= visible == null;
                if (visible == true && Busy(document)) return "DOCUMENT_BUSY";
            }
            foreach (var element in navigation)
            {
                bool? visible = Visible(element);
                if (visible == false) continue;
                bool? enabled = Optional(() => element.Properties.IsEnabled.TryGetValue(out var value) ? (bool?)value : null, null);
                if (enabled == false) continue;
                unavailable |= visible == null || enabled == null;
                if (visible != true || enabled != true) continue;
                string signal = StopSignal(element);
                if (signal != "NONE") return signal;
            }
            return unavailable ? "UIA_STATE_UNAVAILABLE" : "NONE";
        }
        private static bool IsLoading(string signal) => signal is not ("NONE" or "UIA_STATE_UNAVAILABLE");

        private static bool? Visible(AutomationElement element) => Visibility(
            () => element.Properties.IsOffscreen.TryGetValue(out var value) ? value : null,
            () => element.Patterns.LegacyIAccessible.TryGetPattern(out var legacy) && legacy.State.TryGetValue(out var state)
                ? Convert.ToUInt32(state) : null);

        internal static bool? Visibility(Func<bool?> offscreen, Func<uint?> legacyState)
        {
            bool? hidden = Optional(offscreen, null);
            if (hidden.HasValue) return !hidden.Value;
            uint? state = Optional(legacyState, null);
            // Unknown visibility is never treated as proof that an element is on screen.
            return state.HasValue ? (state.Value & 0x18000u) == 0 : null;
        }
        private static bool Busy(AutomationElement element) => BrowserElementRules.IsBusy(Optional(() =>
            element.Patterns.LegacyIAccessible.TryGetPattern(out var legacy) ? Convert.ToUInt32(legacy.State.ValueOrDefault) : 0u, 0u));

        internal static T Optional<T>(Func<T> read, T fallback)
        {
            try { return read(); }
            catch (FlaUI.Core.Exceptions.PropertyNotSupportedException)
            {
                return fallback;
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x80040201))
            {
                // Some MSAA/UIA bridges fail optional property/pattern retrieval while loading.
                // Do not discard every other available signal or reset navigation state.
                Debug.WriteLine($"UIA optional property unavailable: 0x{ex.HResult:X8} {ex.Message}");
                return fallback;
            }
        }
        private static bool IsNavigationButton(AutomationElement element)
        {
            string name = Optional(() => element.Properties.Name.ValueOrDefault, "") ?? "";
            return IsStop(element) || name.Contains("Reload", StringComparison.OrdinalIgnoreCase) || name.Contains("새로") ||
                Optional(() => element.Properties.AutomationId.ValueOrDefault, "") is "reload-button" or "stop-reload-button";
        }
        private static string Key(AutomationElement element) => string.Join(',', element.Properties.RuntimeId.ValueOrDefault ?? []);
        private static string? Value(AutomationElement element)
        {
            string? value = Optional(() => element.Patterns.Value.TryGetPattern(out var pattern) ?
                pattern.Value.ValueOrDefault : null, (string?)null);
            if (!string.IsNullOrEmpty(value)) return value;
            string? legacyValue = Optional(() => element.Patterns.LegacyIAccessible.TryGetPattern(out var legacy) ?
                legacy.Value.ValueOrDefault : null, (string?)null);
            if (!string.IsNullOrEmpty(legacyValue)) return legacyValue;
            return BrowserElementRules.ReadValue(value, legacyValue,
                Optional(() => element.Properties.AutomationId.ValueOrDefault, "") ?? "",
                Optional(() => element.Properties.Name.ValueOrDefault, (string?)null));
        }
        private static string? AbsoluteAddress(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile) return value;
            if (BrowserBlockPolicy.TryHost(value, out _)) return value;
            if (!value.Contains(' ') && value.Contains('.') && BrowserBlockPolicy.TryHost("https://" + value, out _)) return "https://" + value;
            return null;
        }

        public bool Redirect(BlockDecision decision, string destination)
        {
            // Reuse the elements discovered by Read, but fetch live properties before every action.
            // Consume the capture: a failed attempt must wait for a fresh Read rather than reuse stale elements.
            var capture = lastCapture;
            lastCapture = null;
            if (capture == null || capture.Snapshot != decision.Observation) return false;
            if (!SameTarget(capture)) return false;
            var address = capture.Address;
            if (capture.Document != null && Visible(capture.Document) != true) return false;
            var current = new BrowserObservation(capture.Snapshot.Window, capture.Snapshot.Tab,
                capture.Document == null ? null : Value(capture.Document), AbsoluteAddress(Value(address)),
                address.Properties.HasKeyboardFocus.ValueOrDefault,
                IsLoading(LoadingSignal(capture.Selected, capture.Document, capture.NavigationButtons)));
            if (BrowserRedirectVerification.IsDestination(current.AddressUrl, destination) ||
                BrowserRedirectVerification.IsDestination(current.DocumentUrl, destination))
                return false;
            if (!BrowserBlockPolicy.CanRedirect(decision, current, ModifierDown())) return false;
            if (GetForegroundWindow() != current.Window) return false;
            var originalFocus = automation.FocusedElement();
            bool focusedByUs = false;
            bool submitted = false;
            try
            {
                if (current.Loading)
                {
                    var stopped = StopLoading(capture, destination);
                    if (stopped == null) return false;
                    capture = stopped;
                    address = capture.Address;
                    if (!SameTarget(capture) || ModifierDown()) return false;
                    // A stop command can revert the address or detach document/button elements.
                    // Drop cached discovery data; retain this capture only for the current guarded action.
                    cachedCapture = null;
                }
                address.Focus();
                focusedByUs = true;
                if (!SameTarget(capture, checkAddress: false)) return false;
                // A ComboBox may delegate focus to an Edit child or replace its editor on focus.
                var editor = FocusedAddress(capture);
                if (editor == null) return false;
                // Obtain patterns after focusing instead of keeping a pre-focus editor/pattern alive.
                if (!SetAddress(editor, destination) && !SetAddress(address, destination))
                    return false;
                if (!SameTarget(capture, checkAddress: false)) return false;
                // Firefox can report the previous accessible value for a few milliseconds after
                // SetValue returns. Re-read the focused editor rather than aborting before Enter.
                string confirmed = ConfirmAddress(() =>
                {
                    if (!SameTarget(capture, checkAddress: false) || ModifierDown())
                        return ("TARGET_CHANGED_OR_MODIFIER_PRESSED", false);
                    var after = FocusedAddress(capture);
                    if (after == null) return ("ADDRESS_FOCUS_LOST", false);
                    string? editorValue = Value(after);
                    string? addressValue = Value(address);
                    return (editorValue == destination || addressValue == destination
                        ? "CONFIRMED" : "ADDRESS_VALUE_NOT_CONFIRMED", true);
                }, capture.Browser == "firefox" ? TimeSpan.FromMilliseconds(250) : TimeSpan.Zero);
                if (confirmed != "CONFIRMED") return false;
                if (ModifierDown()) return false;
                FlaUI.Core.Input.Keyboard.Type(VirtualKeyShort.RETURN);
                submitted = true;
                return true;
            }
            finally
            {
                // After Enter, let the browser move focus as part of navigation. Focusing the
                // previous document during submission can interfere with the new navigation.
                if (!submitted && focusedByUs && originalFocus != null)
                {
                    try
                    {
                        if (SameTarget(capture, checkAddress: false) && !ModifierDown() &&
                            (capture.Document == null || Value(capture.Document) == current.DocumentUrl))
                        {
                            var editor = FocusedAddress(capture);
                            string? text = editor == null ? null : Value(editor) ?? Value(address);
                            if (editor != null && (text == destination || AbsoluteAddress(text) == current.AddressUrl))
                            {
                                originalFocus.Focus();
                            }
                        }
                    }
                    catch (Exception ex) { Debug.WriteLine($"UIA focus restore: {ex.Message}"); }
                }
            }
        }

        internal static string ConfirmAddress(Func<(string Result, bool CanWait)> read, TimeSpan timeout)
        {
            long started = Stopwatch.GetTimestamp();
            while (true)
            {
                var current = read();
                if (current.Result == "CONFIRMED" || !current.CanWait || Stopwatch.GetElapsedTime(started) >= timeout)
                    return current.Result;
                Thread.Sleep(10);
            }
        }

        private CaptureResult? StopLoading(CaptureResult capture, string destination)
        {
            if (!SameTarget(capture) || ModifierDown() || FocusedAddress(capture) != null) return null;
            if (BrowserRedirectVerification.IsDestination(AbsoluteAddress(Value(capture.Address)), destination)) return null;
            // Avoid asynchronously invoking a Stop control that may execute after Enter.
            // Submit Esc first, then wait for the browser's loading signal to clear.
            FlaUI.Core.Input.Keyboard.Type(VirtualKeyShort.ESCAPE);
            long started = Stopwatch.GetTimestamp();
            // Firefox detaches document/button providers during Stop. Rebuild once after Esc
            // and use the new address/editor as well, while preserving the original tab identity.
            CaptureResult? active = capture.Browser == "firefox" ? null : capture;
            while (Stopwatch.GetElapsedTime(started) < TimeSpan.FromMilliseconds(500))
            {
                if (active == null)
                {
                    active = Capture();
                }
                if (active == null || active.Snapshot.Window != capture.Snapshot.Window || active.Snapshot.Tab != capture.Snapshot.Tab ||
                    !SameTarget(active) || ModifierDown() || FocusedAddress(active) != null) return null;
                if (BrowserRedirectVerification.IsDestination(AbsoluteAddress(Value(active.Address)), destination)) return null;
                string signal = LoadingSignal(active.Selected, active.Document, active.NavigationButtons);
                if (signal == "NONE")
                {
                    return active;
                }
                if (signal == "UIA_STATE_UNAVAILABLE") active = null;
                Thread.Sleep(25);
            }
            return null;
        }

        private static bool SetAddress(AutomationElement element, string destination)
        {
            if (element.Patterns.Value.TryGetPattern(out var value))
            {
                if (value.IsReadOnly.Value) return false;
                value.SetValue(destination);
                return true;
            }
            if (element.Patterns.LegacyIAccessible.TryGetPattern(out var legacy))
            {
                legacy.SetValue(destination);
                return true;
            }
            return false;
        }

        private AutomationElement? FocusedAddress(CaptureResult capture)
        {
            if (GetForegroundWindow() != capture.Snapshot.Window) return null;
            var focused = automation.FocusedElement();
            if (focused == null || !focused.Properties.HasKeyboardFocus.ValueOrDefault) return null;
            string addressKey = capture.AddressKey;
            string addressId = capture.AddressId;
            bool inAddress = false;
            var walker = automation.TreeWalkerFactory.GetRawViewWalker();
            int remaining = 16;
            // Inspect only the focused element's bounded ancestry, never rescan the browser tree.
            for (AutomationElement? node = focused, parent = null; node != null; node = parent)
            {
                if (node.ControlType == ControlType.Document) return null;
                inAddress |= !string.IsNullOrEmpty(addressKey) && Key(node) == addressKey ||
                    !string.IsNullOrEmpty(addressId) && node.Properties.AutomationId.ValueOrDefault == addressId &&
                    IsAddress(node, capture.Browser);
                if (node.Properties.NativeWindowHandle.ValueOrDefault == capture.Snapshot.Window)
                    return inAddress ? focused : null;
                parent = walker.GetParent(node);
                if (--remaining == 0) break;
            }
            return null;
        }

        private static bool SameTarget(CaptureResult capture, bool checkAddress = true)
        {
            if (GetForegroundWindow() != capture.Snapshot.Window || IsIconic(capture.Snapshot.Window) ||
                checkAddress && (Visible(capture.Address) != true || !capture.Address.Properties.IsEnabled.ValueOrDefault)) return false;
            if (capture.Selected != null)
            {
                return Visible(capture.Selected) == true && Key(capture.Selected) == capture.Snapshot.Tab &&
                    capture.Selected.Patterns.SelectionItem.TryGetPattern(out var selected) && selected.IsSelected.Value;
            }
            // When the tab strip is unavailable, require the original visible document as tab identity.
            return capture.Document != null && Visible(capture.Document) == true &&
                Key(capture.Document) == capture.Snapshot.Tab;
        }

        private static bool ModifierDown() => new[] { 0x10, 0x11, 0x12, 0x5b, 0x5c }.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0);
        private void ClearSubscriptions()
        {
            lastCapture = null;
            cachedCapture = null;
            foreach (var subscription in subscriptions) { try { subscription.Dispose(); } catch (Exception ex) { Debug.WriteLine(ex); } }
            subscriptions.Clear();
        }
        public void Dispose() { ClearSubscriptions(); automation.Dispose(); }
        [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint hwnd);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    }
}

