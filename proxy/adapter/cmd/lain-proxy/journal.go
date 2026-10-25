package main

import (
	"encoding/json"
	"errors"
	"flag"
	"io"
	"lain.local/proxy/internal/lain"
	"os"
)

func isJournalMaintenanceCommand(command string) bool {
	switch command {
	case "journal-inspect", "journal-recover-copy", "journal-segment-copy", "journal-export-flat":
		return true
	default:
		return false
	}
}

func runJournalMaintenance(args []string, output io.Writer) error {
	if len(args) == 0 {
		return errors.New("journal maintenance command required")
	}
	flags := flag.NewFlagSet(args[0], flag.ContinueOnError)
	flags.SetOutput(io.Discard)
	path := flags.String("journal", "", "absolute offline journal path")
	destination := flags.String("destination", "", "absolute new private recovery directory")
	maxBytes := flags.Int64("max-segment-bytes", lain.DefaultJournalSegmentBytes, "maximum bytes per segment (4096 to 1073741824)")
	if err := flags.Parse(args[1:]); err != nil {
		return err
	}
	if flags.NArg() != 0 || *path == "" {
		return errors.New("--journal absolute path required")
	}
	if args[0] != "journal-segment-copy" {
		var invalid bool
		flags.Visit(func(f *flag.Flag) {
			if f.Name == "max-segment-bytes" {
				invalid = true
			}
		})
		if invalid {
			return errors.New("--max-segment-bytes is only valid for journal-segment-copy")
		}
	}
	var report any
	var err error
	switch args[0] {
	case "journal-inspect":
		if *destination != "" {
			return errors.New("inspection does not accept a destination")
		}
		if info, statErr := os.Lstat(*path); statErr == nil && info.IsDir() {
			report, err = lain.InspectSegmentedJournal(*path)
		} else {
			report, err = lain.InspectJournal(*path)
		}
	case "journal-recover-copy":
		if *destination == "" {
			return errors.New("--destination absolute new directory required; the original will be retained")
		}
		if info, statErr := os.Lstat(*path); statErr == nil && info.IsDir() {
			report, err = lain.RecoverSegmentedJournalCopy(*path, *destination)
		} else {
			report, err = lain.RecoverJournalCopy(*path, *destination)
		}
	case "journal-segment-copy":
		report, err = lain.SegmentJournalCopy(*path, *destination, *maxBytes)
	case "journal-export-flat":
		report, err = lain.ExportJournalFlat(*path, *destination)
	default:
		return errors.New("unknown journal maintenance command")
	}
	if err != nil {
		return err
	}
	return json.NewEncoder(output).Encode(report)
}
