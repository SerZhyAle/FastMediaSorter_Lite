package main

import (
	"context"
	"io"
	"strconv"
	"testing"
	"time"

	"fms/exchange-server/internal/config"
)

func TestConsoleAndServiceCoreReleaseTheAwakeRequest(t *testing.T) {
	old := holdHostAwake
	defer func() { holdHostAwake = old }()
	for _, console := range []bool{true, false} {
		for _, enabled := range []bool{false, true} {
			cfgPath, _, port := prepare(t)
			cfg, err := config.Load(cfgPath)
			if err != nil {
				t.Fatal(err)
			}
			cfg.KeepAwake = enabled
			if err := config.Save(cfgPath, cfg); err != nil {
				t.Fatal(err)
			}
			starts, stops := 0, 0
			holdHostAwake = func(on bool) (func(), bool, error) {
				if on {
					starts++
				}
				return func() {
					if on {
						stops++
					}
				}, on, nil
			}
			ctx, cancel := context.WithCancel(context.Background())
			done := make(chan int, 1)
			go func() { done <- serveCore(ctx, command{config: cfgPath}, console, io.Discard, io.Discard) }()
			waitListening(t, "127.0.0.1:"+strconv.Itoa(port))
			cancel()
			select {
			case code := <-done:
				if code != exitOK {
					t.Fatal(code)
				}
			case <-time.After(10 * time.Second):
				t.Fatal("host did not stop")
			}
			expected := 0
			if enabled {
				expected = 1
			}
			if starts != expected || stops != expected {
				t.Fatalf("console=%v enabled=%v starts=%d stops=%d", console, enabled, starts, stops)
			}
		}
	}
}
