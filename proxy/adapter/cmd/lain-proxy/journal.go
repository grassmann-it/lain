package main

import (
	"encoding/json"
	"errors"
	"flag"
	"io"
	"lain.local/proxy/internal/lain"
)

func runJournalMaintenance(args []string, output io.Writer) error {
	if len(args) == 0 {
		return errors.New("journal maintenance command required")
	}
	flags := flag.NewFlagSet(args[0], flag.ContinueOnError)
	flags.SetOutput(io.Discard)
	path := flags.String("journal", "", "absolute offline journal path")
	destination := flags.String("destination", "", "absolute new private recovery directory")
	if err := flags.Parse(args[1:]); err != nil {
		return err
	}
	if flags.NArg() != 0 || *path == "" {
		return errors.New("--journal absolute path required")
	}
	var report lain.JournalInspection
	var err error
	switch args[0] {
	case "journal-inspect":
		if *destination != "" {
			return errors.New("inspection does not accept a destination")
		}
		report, err = lain.InspectJournal(*path)
	case "journal-recover-copy":
		if *destination == "" {
			return errors.New("--destination absolute new directory required; the original will be retained")
		}
		report, err = lain.RecoverJournalCopy(*path, *destination)
	default:
		return errors.New("unknown journal maintenance command")
	}
	if err != nil {
		return err
	}
	return json.NewEncoder(output).Encode(report)
}
