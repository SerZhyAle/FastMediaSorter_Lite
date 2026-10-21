package rendezvous

func adminResult(ok bool) string {
	if ok {
		return "OK"
	}
	return "Failed"
}
