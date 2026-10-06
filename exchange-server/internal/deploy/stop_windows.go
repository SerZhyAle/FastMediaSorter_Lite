package deploy

import (
	"context"
	"errors"
	"fmt"
	"os"
	"os/signal"
	"syscall"
	"unsafe"

	"golang.org/x/sys/windows"
)

// stopEventAttributes gives the named event to the current user, SYSTEM and Administrators only. The
// default descriptor of an elevated process leaves out the user's filtered token, so a server
// started elevated could not be stopped from the user's Share Manager; spelling the owner out
// removes that surprise without opening the event to anybody else.
func stopEventAttributes() (*windows.SecurityAttributes, error) {
	user, err := windows.GetCurrentProcessToken().GetTokenUser()
	if err != nil {
		return nil, fmt.Errorf("cannot read the current user: %w", err)
	}
	sddl := fmt.Sprintf("D:P(A;;GA;;;%s)(A;;GA;;;SY)(A;;GA;;;BA)", user.User.Sid.String())
	sd, err := windows.SecurityDescriptorFromString(sddl)
	if err != nil {
		return nil, err
	}
	sa := &windows.SecurityAttributes{SecurityDescriptor: sd}
	sa.Length = uint32(unsafe.Sizeof(*sa))
	return sa, nil
}

// NotifyStop returns a context cancelled by Ctrl+C, SIGTERM, or - when configPath is not empty -
// by the named stop event of that config being set (`fms-exchange-server stop`). The event is
// created here, so it exists exactly while a console server is up. A non-nil error means the
// event could not be created: the context still works for signals, and the caller should say so.
// The returned cancel function also waits for the watcher to release the event, so a restart in
// the same process never meets the previous run's signalled event.
func NotifyStop(parent context.Context, configPath string) (context.Context, context.CancelFunc, error) {
	ctx, cancel := signal.NotifyContext(parent, os.Interrupt, syscall.SIGTERM)
	if configPath == "" {
		return ctx, cancel, nil
	}
	name, err := windows.UTF16PtrFromString(StopEventName(configPath))
	if err != nil {
		return ctx, cancel, err
	}
	sa, err := stopEventAttributes()
	if err != nil {
		return ctx, cancel, err
	}
	ev, err := windows.CreateEvent(sa, 1, 0, name) // manual reset: a stop stays a stop
	if ev == 0 {
		return ctx, cancel, fmt.Errorf("cannot create the stop event: %w", err)
	}
	// ERROR_ALREADY_EXISTS with a valid handle: another console instance of this config owns the
	// event. Share it and leave its state alone; the listen of this instance will be refused anyway.
	quit, err := windows.CreateEvent(nil, 1, 0, nil)
	if err != nil {
		_ = windows.CloseHandle(ev)
		return ctx, cancel, fmt.Errorf("cannot create the stop watcher: %w", err)
	}

	finished := make(chan struct{})
	go func() {
		defer close(finished)
		helperDone := make(chan struct{})
		go func() { // wakes the wait when the context ends for any other reason
			defer close(helperDone)
			<-ctx.Done()
			_ = windows.SetEvent(quit)
		}()
		r, _ := windows.WaitForMultipleObjects([]windows.Handle{ev, quit}, false, windows.INFINITE)
		if r == windows.WAIT_OBJECT_0 { // the stop event
			cancel()
		}
		<-helperDone
		_ = windows.CloseHandle(ev)
		_ = windows.CloseHandle(quit)
	}()
	return ctx, func() { cancel(); <-finished }, nil
}

// RequestStop sets the stop event of the console server running for configPath. It returns
// ErrNotRunning when there is no such event (nothing is running for that config).
func RequestStop(configPath string) error {
	name, err := windows.UTF16PtrFromString(StopEventName(configPath))
	if err != nil {
		return err
	}
	ev, err := windows.OpenEvent(windows.EVENT_MODIFY_STATE, false, name)
	if err != nil {
		if errors.Is(err, windows.ERROR_FILE_NOT_FOUND) {
			return ErrNotRunning
		}
		return fmt.Errorf("cannot open the stop event: %w", err)
	}
	defer windows.CloseHandle(ev)
	return windows.SetEvent(ev)
}
