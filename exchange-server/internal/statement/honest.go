package statement

import "fms/exchange-server/internal/wire"

// The honest text of item DE (DEVICE-EXCHANGE 27, ticket 234). Once a server advertises `sealed`, content
// that goes through it is encrypted on the devices and the server cannot open it, so the points of Points
// ("the operator can read it in the clear") describe a server that does not seal and no longer hold. A
// server that advertises `sealed` says the text below instead.
//
// The contract gives the text in three languages and asks every product that describes the exchange
// server to use it "not reworded and not shortened": the strings are the contract's own, copied byte for
// byte. A language the contract does not give has no entry here and none is invented; a surface that
// translates into more languages keeps showing those the English text until the contract gives one.

// HonestID names the statement of a sealed server for a surface that keys its translations by point id.
// The ids of Points are never reused for it.
const HonestID = "e2e-honest"

const (
	honestEN = `The exchange server connects your devices, so it knows the names of your account, devices, folders and broadcasts, sees which devices are online, and how much is sent and when. It cannot open what goes through it: folders and their files, broadcasts and the clipboard are encrypted on your device and opened only on your other devices or by the people you gave access to. To look inside, whoever runs the server would have to add a device of their own to your account, and it would appear in your list of devices.`
	honestRU = `Сервер обмена соединяет ваши устройства, поэтому знает имена вашего аккаунта, устройств, папок и трансляций, видит, какие устройства в сети, и сколько и когда передаётся. Открыть то, что идёт через него, он не может: папки и их файлы, трансляции и буфер шифруются на вашем устройстве и открываются только на других ваших устройствах или у тех, кому вы дали доступ. Чтобы заглянуть внутрь, тому, кто управляет сервером, пришлось бы добавить в ваш аккаунт своё устройство, и оно появилось бы в списке ваших устройств.`
	honestUK = `Сервер обміну з'єднує ваші пристрої, тому знає імена вашого акаунта, пристроїв, папок і трансляцій, бачить, які пристрої в мережі, і скільки й коли передається. Відкрити те, що йде через нього, він не може: папки та їхні файли, трансляції й буфер шифруються на вашому пристрої та відкриваються лише на інших ваших пристроях або в тих, кому ви дали доступ. Щоб зазирнути всередину, тому, хто керує сервером, довелося б додати до вашого акаунта свій пристрій, і він з'явився б у списку ваших пристроїв.`
)

// HonestLanguages are the language codes the contract gives the honest text in.
var HonestLanguages = []string{"en", "ru", "uk"}

// Honest returns the honest text of item DE in a language; ok is false for a language the contract does
// not give it in.
func Honest(lang string) (text string, ok bool) {
	switch lang {
	case "en":
		return honestEN, true
	case "ru":
		return honestRU, true
	case "uk":
		return honestUK, true
	}
	return "", false
}

// Sealed reports whether a list of `welcome.features` advertises sealed content (item DP).
func Sealed(features []string) bool {
	for _, f := range features {
		if f == wire.FeatureSealed {
			return true
		}
	}
	return false
}

// TextFor is the statement a server with these features makes, in the English source: the honest text of
// item DE when it advertises `sealed`, the points of Text otherwise.
func TextFor(features []string) string {
	if Sealed(features) {
		return honestEN
	}
	return Text()
}

// WithRetentionFor is TextFor with the log-retention sentence of WithRetention.
func WithRetentionFor(features []string, days int) string {
	return TextFor(features) + retentionNote(days)
}
