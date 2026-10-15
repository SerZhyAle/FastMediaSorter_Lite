package deploy

import "sync"

// keepAwake holds a thread-scoped Windows request on one OS thread until release.
// The seam is shared by console and service hosts and permits tests without changing power policy.
func keepAwake(enabled bool, request func(bool) error, enter func(), leave func()) (func(), bool, error) {
	if !enabled {
		return func() {}, false, nil
	}
	stop, done, ready := make(chan struct{}), make(chan struct{}), make(chan error, 1)
	go func() {
		enter()
		defer leave()
		defer close(done)
		err := request(true)
		ready <- err
		if err != nil {
			return
		}
		<-stop
		_ = request(false)
	}()
	err := <-ready
	var once sync.Once
	release := func() { once.Do(func() { close(stop) }); <-done }
	if err != nil {
		release()
		return func() {}, false, err
	}
	return release, true, nil
}
