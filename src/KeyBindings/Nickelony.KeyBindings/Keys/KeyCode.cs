using System.Diagnostics.CodeAnalysis;

namespace Nickelony.KeyBindings;

/// <summary>Identifies a keyboard key that can be the primary key of a <see cref="KeyStroke"/>.</summary>
/// <remarks>
/// Values are dense from <c>1</c>, package-owned, and a persisted contract; an adapter maps through an explicit table, not an integer cast.
/// Member names are the canonical serialized names - the unshifted US character or physical position, not the active layout's label.
/// <c>0</c> (the <see langword="default"/>) is not a member. The set keeps every desktop key a host can bind as a shortcut, including the browser,
/// media and application-launch keys, and leaves out only keys that can never be a shortcut, such as the modifier and lock keys. A toolkit adapter
/// converts a toolkit key outside the set to <see langword="null"/>.
/// </remarks>
public enum KeyCode
{
	Cancel = 1,

	Backspace = 2,

	Tab = 3,

	/// <summary>The numeric-keypad Clear key, pressed with Num Lock off.</summary>
	Clear = 4,

	Enter = 5,

	Pause = 6,

	/// <summary>The Kana (Hangul) mode key.</summary>
	KanaMode = 7,

	JunjaMode = 8,

	FinalMode = 9,

	/// <summary>The Hanja (Kanji) mode key.</summary>
	HanjaMode = 10,

	Escape = 11,

	ImeConvert = 12,

	ImeNonConvert = 13,

	ImeAccept = 14,

	ImeModeChange = 15,

	Space = 16,

	PageUp = 17,

	PageDown = 18,

	End = 19,

	Home = 20,

	Left = 21,

	Up = 22,

	Right = 23,

	Down = 24,

	Select = 25,

	Print = 26,

	Execute = 27,

	PrintScreen = 28,

	Insert = 29,

	Delete = 30,

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

	A = 42,

	B = 43,

	C = 44,

	D = 45,

	E = 46,

	F = 47,

	G = 48,

	H = 49,

	I = 50,

	J = 51,

	K = 52,

	L = 53,

	M = 54,

	N = 55,

	O = 56,

	P = 57,

	Q = 58,

	R = 59,

	S = 60,

	T = 61,

	U = 62,

	V = 63,

	W = 64,

	X = 65,

	Y = 66,

	Z = 67,

	/// <summary>The application (menu) key.</summary>
	Apps = 68,

	Sleep = 69,

	NumPad0 = 70,

	NumPad1 = 71,

	NumPad2 = 72,

	NumPad3 = 73,

	NumPad4 = 74,

	NumPad5 = 75,

	NumPad6 = 76,

	NumPad7 = 77,

	NumPad8 = 78,

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
	[SuppressMessage(
		"Naming",
		"CA1720:Identifier contains type name",
		Justification = "The member name is the canonical serialized name of the keypad decimal key and follows the established .NET keyboard naming."
	)]
	Decimal = 84,

	/// <summary>The divide key on the numeric keypad.</summary>
	Divide = 85,

	F1 = 86,

	F2 = 87,

	F3 = 88,

	F4 = 89,

	F5 = 90,

	F6 = 91,

	F7 = 92,

	F8 = 93,

	F9 = 94,

	F10 = 95,

	F11 = 96,

	F12 = 97,

	F13 = 98,

	F14 = 99,

	F15 = 100,

	F16 = 101,

	F17 = 102,

	F18 = 103,

	F19 = 104,

	F20 = 105,

	F21 = 106,

	F22 = 107,

	F23 = 108,

	F24 = 109,

	BrowserBack = 110,

	BrowserForward = 111,

	BrowserRefresh = 112,

	BrowserStop = 113,

	BrowserSearch = 114,

	BrowserFavorites = 115,

	BrowserHome = 116,

	VolumeMute = 117,

	VolumeDown = 118,

	VolumeUp = 119,

	MediaNextTrack = 120,

	MediaPreviousTrack = 121,

	MediaStop = 122,

	MediaPlayPause = 123,

	LaunchMail = 124,

	SelectMedia = 125,

	LaunchApplication1 = 126,

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
	IntlBackslash = 142,
}
