using System.Diagnostics.CodeAnalysis;

namespace Nickelony.KeyBindings;

/// <summary>
/// Identifies a keyboard key that can be the primary key of a <see cref="KeyCombo"/>.
/// </summary>
/// <remarks>
/// <para>
/// The values are owned by this package and are dense: they start at <c>1</c> and run consecutively
/// through the members declared below, so the set carries no numbering and no gaps inherited from any
/// desktop framework. A framework adapter translates through an explicit table rather than an integer
/// cast, and no adapter may assume the values match its own key enum. The numbering is part of this
/// package's contract; it is not the Win32 virtual-key numbering.
/// </para>
/// <para>
/// The member names are the canonical serialized names for persisted overrides. They describe the
/// physical key by its unshifted US character or position, such as <c>Slash</c>, <c>LeftBracket</c>,
/// and <c>IntlBackslash</c>, rather than the localized label of the active layout. Alias names that
/// some frameworks use for the same values, such as <c>Capital</c>, <c>Prior</c>, <c>Next</c>,
/// <c>Snapshot</c>, or <c>Oem1</c>, are not repeated.
/// </para>
/// <para>
/// The set is a desktop-key superset modeled on the WPF/desktop key set, not a portable minimum: a
/// toolkit adapter maps the subset its own key model exposes and reports the rest as unmapped. It keeps
/// every key a host can bind as a command shortcut, including the Windows browser, media, and
/// application-launch keys, which an editor rarely binds but can carry through unchanged. It drops only
/// keys that can never be a shortcut: modifier keys, the lock keys (Caps Lock, Num Lock, Scroll Lock),
/// the processing pseudo-keys (System, ImeProcessed, DeadCharProcessed), legacy keys (LineFeed, the
/// DBE/OEM conversion keys 157-170 such as OemAttn and Pa1, and the legacy OEM Clear key, which is a
/// different key from the numeric-keypad <c>Clear</c> this set keeps), <c>IntlRo</c>, the
/// Korean <c>Lang1</c>/<c>Lang2</c> keys, and function keys beyond <c>F24</c>. <c>Oem8</c> stays even
/// though it has no standard unshifted character and a display formatter cannot label it from one. The
/// default value (<c>0</c>) is not a defined member.
/// </para>
/// </remarks>
public enum KeyCode
{
	/// <summary>The Cancel key.</summary>
	Cancel = 1,

	/// <summary>The Backspace key.</summary>
	Backspace = 2,

	/// <summary>The Tab key.</summary>
	Tab = 3,

	/// <summary>The numeric-keypad Clear key, pressed with Num Lock off.</summary>
	Clear = 4,

	/// <summary>The Enter key.</summary>
	Enter = 5,

	/// <summary>The Pause key.</summary>
	Pause = 6,

	/// <summary>The Kana (Hangul) mode key.</summary>
	KanaMode = 7,

	/// <summary>The Junja mode key.</summary>
	JunjaMode = 8,

	/// <summary>The final mode key.</summary>
	FinalMode = 9,

	/// <summary>The Hanja (Kanji) mode key.</summary>
	HanjaMode = 10,

	/// <summary>The Escape key.</summary>
	Escape = 11,

	/// <summary>The IME convert key.</summary>
	ImeConvert = 12,

	/// <summary>The IME non-convert key.</summary>
	ImeNonConvert = 13,

	/// <summary>The IME accept key.</summary>
	ImeAccept = 14,

	/// <summary>The IME mode change key.</summary>
	ImeModeChange = 15,

	/// <summary>The Spacebar.</summary>
	Space = 16,

	/// <summary>The Page Up key.</summary>
	PageUp = 17,

	/// <summary>The Page Down key.</summary>
	PageDown = 18,

	/// <summary>The End key.</summary>
	End = 19,

	/// <summary>The Home key.</summary>
	Home = 20,

	/// <summary>The Left Arrow key.</summary>
	Left = 21,

	/// <summary>The Up Arrow key.</summary>
	Up = 22,

	/// <summary>The Right Arrow key.</summary>
	Right = 23,

	/// <summary>The Down Arrow key.</summary>
	Down = 24,

	/// <summary>The Select key.</summary>
	Select = 25,

	/// <summary>The Print key.</summary>
	Print = 26,

	/// <summary>The Execute key.</summary>
	Execute = 27,

	/// <summary>The Print Screen key.</summary>
	PrintScreen = 28,

	/// <summary>The Insert key.</summary>
	Insert = 29,

	/// <summary>The Delete key.</summary>
	Delete = 30,

	/// <summary>The Help key.</summary>
	Help = 31,

	/// <summary>The 0 key on the main keyboard row.</summary>
	D0 = 32,

	/// <summary>The 1 key on the main keyboard row.</summary>
	D1 = 33,

	/// <summary>The 2 key on the main keyboard row.</summary>
	D2 = 34,

	/// <summary>The 3 key on the main keyboard row.</summary>
	D3 = 35,

	/// <summary>The 4 key on the main keyboard row.</summary>
	D4 = 36,

	/// <summary>The 5 key on the main keyboard row.</summary>
	D5 = 37,

	/// <summary>The 6 key on the main keyboard row.</summary>
	D6 = 38,

	/// <summary>The 7 key on the main keyboard row.</summary>
	D7 = 39,

	/// <summary>The 8 key on the main keyboard row.</summary>
	D8 = 40,

	/// <summary>The 9 key on the main keyboard row.</summary>
	D9 = 41,

	/// <summary>The A key.</summary>
	A = 42,

	/// <summary>The B key.</summary>
	B = 43,

	/// <summary>The C key.</summary>
	C = 44,

	/// <summary>The D key.</summary>
	D = 45,

	/// <summary>The E key.</summary>
	E = 46,

	/// <summary>The F key.</summary>
	F = 47,

	/// <summary>The G key.</summary>
	G = 48,

	/// <summary>The H key.</summary>
	H = 49,

	/// <summary>The I key.</summary>
	I = 50,

	/// <summary>The J key.</summary>
	J = 51,

	/// <summary>The K key.</summary>
	K = 52,

	/// <summary>The L key.</summary>
	L = 53,

	/// <summary>The M key.</summary>
	M = 54,

	/// <summary>The N key.</summary>
	N = 55,

	/// <summary>The O key.</summary>
	O = 56,

	/// <summary>The P key.</summary>
	P = 57,

	/// <summary>The Q key.</summary>
	Q = 58,

	/// <summary>The R key.</summary>
	R = 59,

	/// <summary>The S key.</summary>
	S = 60,

	/// <summary>The T key.</summary>
	T = 61,

	/// <summary>The U key.</summary>
	U = 62,

	/// <summary>The V key.</summary>
	V = 63,

	/// <summary>The W key.</summary>
	W = 64,

	/// <summary>The X key.</summary>
	X = 65,

	/// <summary>The Y key.</summary>
	Y = 66,

	/// <summary>The Z key.</summary>
	Z = 67,

	/// <summary>The application (menu) key.</summary>
	Apps = 68,

	/// <summary>The sleep key.</summary>
	Sleep = 69,

	/// <summary>The 0 key on the numeric keypad.</summary>
	NumPad0 = 70,

	/// <summary>The 1 key on the numeric keypad.</summary>
	NumPad1 = 71,

	/// <summary>The 2 key on the numeric keypad.</summary>
	NumPad2 = 72,

	/// <summary>The 3 key on the numeric keypad.</summary>
	NumPad3 = 73,

	/// <summary>The 4 key on the numeric keypad.</summary>
	NumPad4 = 74,

	/// <summary>The 5 key on the numeric keypad.</summary>
	NumPad5 = 75,

	/// <summary>The 6 key on the numeric keypad.</summary>
	NumPad6 = 76,

	/// <summary>The 7 key on the numeric keypad.</summary>
	NumPad7 = 77,

	/// <summary>The 8 key on the numeric keypad.</summary>
	NumPad8 = 78,

	/// <summary>The 9 key on the numeric keypad.</summary>
	NumPad9 = 79,

	/// <summary>The multiply key on the numeric keypad.</summary>
	Multiply = 80,

	/// <summary>The add key on the numeric keypad.</summary>
	Add = 81,

	/// <summary>The separator key on the numeric keypad.</summary>
	Separator = 82,

	/// <summary>The subtract key on the numeric keypad.</summary>
	Subtract = 83,

	/// <summary>The decimal key on the numeric keypad.</summary>
	[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "The member name is the canonical serialized name of the keypad decimal key and follows the established .NET keyboard naming.")]
	Decimal = 84,

	/// <summary>The divide key on the numeric keypad.</summary>
	Divide = 85,

	/// <summary>The F1 function key.</summary>
	F1 = 86,

	/// <summary>The F2 function key.</summary>
	F2 = 87,

	/// <summary>The F3 function key.</summary>
	F3 = 88,

	/// <summary>The F4 function key.</summary>
	F4 = 89,

	/// <summary>The F5 function key.</summary>
	F5 = 90,

	/// <summary>The F6 function key.</summary>
	F6 = 91,

	/// <summary>The F7 function key.</summary>
	F7 = 92,

	/// <summary>The F8 function key.</summary>
	F8 = 93,

	/// <summary>The F9 function key.</summary>
	F9 = 94,

	/// <summary>The F10 function key.</summary>
	F10 = 95,

	/// <summary>The F11 function key.</summary>
	F11 = 96,

	/// <summary>The F12 function key.</summary>
	F12 = 97,

	/// <summary>The F13 function key.</summary>
	F13 = 98,

	/// <summary>The F14 function key.</summary>
	F14 = 99,

	/// <summary>The F15 function key.</summary>
	F15 = 100,

	/// <summary>The F16 function key.</summary>
	F16 = 101,

	/// <summary>The F17 function key.</summary>
	F17 = 102,

	/// <summary>The F18 function key.</summary>
	F18 = 103,

	/// <summary>The F19 function key.</summary>
	F19 = 104,

	/// <summary>The F20 function key.</summary>
	F20 = 105,

	/// <summary>The F21 function key.</summary>
	F21 = 106,

	/// <summary>The F22 function key.</summary>
	F22 = 107,

	/// <summary>The F23 function key.</summary>
	F23 = 108,

	/// <summary>The F24 function key.</summary>
	F24 = 109,

	/// <summary>The browser back key.</summary>
	BrowserBack = 110,

	/// <summary>The browser forward key.</summary>
	BrowserForward = 111,

	/// <summary>The browser refresh key.</summary>
	BrowserRefresh = 112,

	/// <summary>The browser stop key.</summary>
	BrowserStop = 113,

	/// <summary>The browser search key.</summary>
	BrowserSearch = 114,

	/// <summary>The browser favorites key.</summary>
	BrowserFavorites = 115,

	/// <summary>The browser home key.</summary>
	BrowserHome = 116,

	/// <summary>The volume mute key.</summary>
	VolumeMute = 117,

	/// <summary>The volume down key.</summary>
	VolumeDown = 118,

	/// <summary>The volume up key.</summary>
	VolumeUp = 119,

	/// <summary>The next track media key.</summary>
	MediaNextTrack = 120,

	/// <summary>The previous track media key.</summary>
	MediaPreviousTrack = 121,

	/// <summary>The stop media key.</summary>
	MediaStop = 122,

	/// <summary>The play/pause media key.</summary>
	MediaPlayPause = 123,

	/// <summary>The mail launch key.</summary>
	LaunchMail = 124,

	/// <summary>The media select key.</summary>
	SelectMedia = 125,

	/// <summary>The application launch key 1.</summary>
	LaunchApplication1 = 126,

	/// <summary>The application launch key 2.</summary>
	LaunchApplication2 = 127,

	/// <summary>The semicolon key (<c>;</c> and <c>:</c> on US layouts).</summary>
	Semicolon = 128,

	/// <summary>The equals key (<c>=</c> and <c>+</c> on US layouts).</summary>
	Equals = 129,

	/// <summary>The comma key (<c>,</c> and <c>&lt;</c> on US layouts).</summary>
	Comma = 130,

	/// <summary>The minus key (<c>-</c> and <c>_</c> on US layouts).</summary>
	Minus = 131,

	/// <summary>The period key (<c>.</c> and <c>&gt;</c> on US layouts).</summary>
	Period = 132,

	/// <summary>The slash key (<c>/</c> and <c>?</c> on US layouts).</summary>
	Slash = 133,

	/// <summary>The grave accent key (<c>`</c> and <c>~</c> on US layouts).</summary>
	Grave = 134,

	/// <summary>The ABNT C1 key on Brazilian keyboards.</summary>
	AbntC1 = 135,

	/// <summary>The ABNT C2 key on Brazilian keyboards.</summary>
	AbntC2 = 136,

	/// <summary>The left bracket key (<c>[</c> and <c>{</c> on US layouts).</summary>
	LeftBracket = 137,

	/// <summary>The backslash key (<c>\</c> and <c>|</c> on US layouts).</summary>
	Backslash = 138,

	/// <summary>The right bracket key (<c>]</c> and <c>}</c> on US layouts).</summary>
	RightBracket = 139,

	/// <summary>The apostrophe key (<c>'</c> and <c>"</c> on US layouts).</summary>
	Apostrophe = 140,

	/// <summary>The OEM-specific key (layout-dependent).</summary>
	Oem8 = 141,

	/// <summary>The additional backslash key on 102-key (ISO) layouts.</summary>
	IntlBackslash = 142
}
