using System.Runtime.CompilerServices;

// The file readers are internal — callers hold IImportFileReader — and tested directly, because
// what has to be right about them is the parsing of files real suppliers send.
[assembly: InternalsVisibleTo("EwaysVNext.UnitTests")]
