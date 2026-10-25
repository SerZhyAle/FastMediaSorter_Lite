package logx

import (
	"errors"
	"os"
	"path/filepath"
	"strings"
	"time"
)

// IsDayFile accepts only this logger's UTC day files and their single bounded rotation.
func IsDayFile(name string) bool {
	name = strings.TrimSuffix(name, ".1")
	if !strings.HasPrefix(name, "exchange-") || !strings.HasSuffix(name, ".log") {
		return false
	}
	day := strings.TrimSuffix(strings.TrimPrefix(name, "exchange-"), ".log")
	t, err := time.Parse("2006-01-02", day)
	return err == nil && t.Format("2006-01-02") == day
}

// OpenDaily owns a UTC day file, a daily retention sweep and the per-file byte cap.
// The injected clock makes midnight and retention behavior testable without waiting.
func OpenDaily(dir string, maxBytes int64, days int, now func() time.Time) (*Logger, error) {
	if days < 1 || days > 36500 || maxBytes < 1 {
		return nil, errors.New("invalid daily log limits")
	}
	if now == nil {
		now = time.Now
	}
	l := &Logger{dailyDir: dir, max: maxBytes, days: days, now: now, stop: make(chan struct{}), stopped: make(chan struct{})}
	if err := l.dayFile(l.now()); err != nil {
		return nil, err
	}
	if err := l.Sweep(); err != nil {
		_ = l.file.Close()
		return nil, err
	}
	go func() {
		defer close(l.stopped)
		ticker := time.NewTicker(24 * time.Hour)
		defer ticker.Stop()
		for {
			select {
			case <-l.stop:
				return
			case <-ticker.C:
				_ = l.Sweep()
			}
		}
	}()
	return l, nil
}

func (l *Logger) dayFile(at time.Time) error {
	path := filepath.Join(l.dailyDir, "exchange-"+at.UTC().Format("2006-01-02")+".log")
	if path == l.path && l.file != nil {
		return nil
	}
	if l.file != nil {
		_ = l.file.Close()
	}
	l.file, l.w = nil, nil
	l.path, l.size = path, 0
	if err := l.openFile(); err != nil {
		return err
	}
	l.writeBanner()
	return nil
}

// Sweep removes only owned day files outside the configured UTC date window.
func (l *Logger) Sweep() error {
	l.mu.Lock()
	defer l.mu.Unlock()
	today := l.now().UTC()
	if !l.lastSweep.IsZero() && today.Sub(l.lastSweep) < 24*time.Hour {
		return nil
	}
	midnight := time.Date(today.Year(), today.Month(), today.Day(), 0, 0, 0, 0, time.UTC)
	cutoff := midnight.AddDate(0, 0, -(l.days - 1)).Format("2006-01-02")
	entries, err := os.ReadDir(l.dailyDir)
	if err != nil {
		return err
	}
	for _, entry := range entries {
		name := entry.Name()
		if entry.Type().IsRegular() && IsDayFile(name) && name[9:19] < cutoff {
			if err := os.Remove(filepath.Join(l.dailyDir, name)); err != nil {
				return err
			}
		}
	}
	l.lastSweep = today
	return nil
}
