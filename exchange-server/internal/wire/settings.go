package wire

import (
	"errors"
)

// This file is ticket L0296: the application settings standard of DEVICE-EXCHANGE 1.12 (items EN to ER,
// SETTINGS-STANDARD-2026-11-12). Four frames on the control stream: a member's device saves the one
// standard of its account and its program, or reads it back. The names are snake case, unlike every other
// type of this wire: they stand as filed in the vectors and are wire values from 1.12 on.
const (
	TypeSettingsSaveStandard    = "settings_save_standard"
	TypeSettingsSaved           = "settings_saved"
	TypeSettingsGetStandard     = "settings_get_standard"
	TypeSettingsStandardPayload = "settings_standard_payload"

	// FeatureSettings is the `welcome`/`enrolled` feature of item EO: listed when the server keeps standards.
	FeatureSettings = "settings"

	// SettingsFormatVersion is the one `formatVersion` 1.12 defines (item EO).
	SettingsFormatVersion = 1
	// MaxSettingsPayload is the bound of item EO on `settingsPayload`, in bytes of UTF-8.
	MaxSettingsPayload = 65536

	maxAppID = 64
)

// SettingsSaveStandard replaces the standard of the device's account and program (item EO). FormatVersion
// and SettingsPayload are pointers so that an absent member closes the stream (item AN) while a present
// one with an unacceptable value is refused (item EP).
type SettingsSaveStandard struct {
	Head
	AppID           string  `json:"appId"`
	FormatVersion   *int    `json:"formatVersion"`
	SettingsPayload *string `json:"settingsPayload"`
	Timestamp       string  `json:"timestamp,omitempty"` // informational only: never an order, never updatedAt
}

// TypeName is "settings_save_standard".
func (*SettingsSaveStandard) TypeName() string { return TypeSettingsSaveStandard }

// SettingsSaved answers an accepted save: the pair's new version and the server's clock at the save.
type SettingsSaved struct {
	Head
	AppID     string `json:"appId"`
	Version   int64  `json:"version"`
	UpdatedAt string `json:"updatedAt"`
}

// TypeName is "settings_saved".
func (*SettingsSaved) TypeName() string { return TypeSettingsSaved }

// SettingsGetStandard asks for the standard of the device's account and program.
type SettingsGetStandard struct {
	Head
	AppID string `json:"appId"`
}

// TypeName is "settings_get_standard".
func (*SettingsGetStandard) TypeName() string { return TypeSettingsGetStandard }

// SettingsStandardPayload answers a get with the stored standard, as it was received.
type SettingsStandardPayload struct {
	Head
	AppID           string `json:"appId"`
	FormatVersion   int    `json:"formatVersion"`
	SettingsPayload string `json:"settingsPayload"`
	UpdatedAt       string `json:"updatedAt"`
}

// TypeName is "settings_standard_payload".
func (*SettingsStandardPayload) TypeName() string { return TypeSettingsStandardPayload }

// settingsAppIDs maps a registered `appId` to the `product` of the device that may use it (item EN).
var settingsAppIDs = map[string]string{
	"fms_win":       "fms-windows",
	"fms_a":         "fms-android",
	"cyrflip":       "cyrflip-windows",
	"streamsplayer": "streamsplayer-windows",
}

// SettingsAppIDFor is the `appId` a device's `product` maps to, or "" when the product holds none in 1.12.
func SettingsAppIDFor(product string) string {
	for app, p := range settingsAppIDs {
		if p == product {
			return app
		}
	}
	return ""
}

// SettingsAppIDs lists the registered `appId` values (item EN), for the tests and the operator's view.
func SettingsAppIDs() []string { return []string{"fms_win", "fms_a", "cyrflip", "streamsplayer"} }

func (e *SettingsSaveStandard) validate() error {
	if err := required("appId", e.AppID, maxAppID); err != nil {
		return err
	}
	if e.FormatVersion == nil {
		return errors.New("formatVersion is required")
	}
	if e.SettingsPayload == nil {
		return errors.New("settingsPayload is required")
	}
	if e.Timestamp != "" {
		if _, err := ParseTime(e.Timestamp); err != nil {
			return errors.New("timestamp is not a time")
		}
	}
	return nil
}

func (e *SettingsGetStandard) validate() error { return required("appId", e.AppID, maxAppID) }

func (e *SettingsSaved) validate() error {
	if err := required("appId", e.AppID, maxAppID); err != nil {
		return err
	}
	if e.Version < 1 {
		return errors.New("version is at least 1")
	}
	if _, err := ParseTime(e.UpdatedAt); err != nil {
		return errors.New("updatedAt is not a time")
	}
	return nil
}

func (e *SettingsStandardPayload) validate() error {
	if err := required("appId", e.AppID, maxAppID); err != nil {
		return err
	}
	if e.FormatVersion < 1 {
		return errors.New("formatVersion is at least 1")
	}
	if _, err := ParseTime(e.UpdatedAt); err != nil {
		return errors.New("updatedAt is not a time")
	}
	return nil
}
