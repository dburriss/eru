#nowarn "0044"
module Eru.Tui.Browse.ConfigPane

open Terminal.Gui.App
open Terminal.Gui.Drawing
open Terminal.Gui.ViewBase
open Terminal.Gui.Views

type ConfigPane() as this =
    inherit View()

    let label = new Label()

    do
        this.CanFocus <- true
        BrowseTheme.apply BrowseTheme.Main this

        label.Text <- "Config — coming soon"
        label.X <- Pos.Center()
        label.Y <- Pos.Center()
        BrowseTheme.apply BrowseTheme.Muted label
        this.Add(label :> View) |> ignore

    member _.FocusContent() = this.SetFocus() |> ignore
