#nowarn "0044"
module Eru.Cli.Browse.AddSourceDialog

open Terminal.Gui.App
open Terminal.Gui.ViewBase
open Terminal.Gui.Views

type SourceInput = {
    Url      : string
    Name     : string option
    Branch   : string option
    BasePath : string option
}

let show () : SourceInput option =
    let mutable result: SourceInput option = None

    let dlg = new Dialog()
    dlg.Title <- "Add Source"
    dlg.Width <- Dim.Absolute 60
    dlg.Height <- Dim.Absolute 16

    let urlLabel    = new Label()
    let urlField    = new TextField()
    let nameLabel   = new Label()
    let nameField   = new TextField()
    let branchLabel = new Label()
    let branchField = new TextField()
    let bpLabel     = new Label()
    let bpField     = new TextField()

    urlLabel.Text    <- "URL (required):"
    nameLabel.Text   <- "Name (optional):"
    branchLabel.Text <- "Branch (optional):"
    bpLabel.Text     <- "Base path (optional):"

    let optStr (s: string) =
        let t = s.Trim()
        if t = "" then None else Some t

    urlLabel.X <- Pos.Absolute 1
    urlLabel.Y <- Pos.Absolute 1

    urlField.X <- Pos.Absolute 1
    urlField.Y <- Pos.Bottom urlLabel
    urlField.Width <- Dim.Fill(Dim.Absolute 2)

    nameLabel.X <- Pos.Absolute 1
    nameLabel.Y <- Pos.Bottom urlField + Pos.Absolute 1

    nameField.X <- Pos.Absolute 1
    nameField.Y <- Pos.Bottom nameLabel
    nameField.Width <- Dim.Fill(Dim.Absolute 2)

    branchLabel.X <- Pos.Absolute 1
    branchLabel.Y <- Pos.Bottom nameField + Pos.Absolute 1

    branchField.X <- Pos.Absolute 1
    branchField.Y <- Pos.Bottom branchLabel
    branchField.Width <- Dim.Fill(Dim.Absolute 2)

    bpLabel.X <- Pos.Absolute 1
    bpLabel.Y <- Pos.Bottom branchField + Pos.Absolute 1

    bpField.X <- Pos.Absolute 1
    bpField.Y <- Pos.Bottom bpLabel
    bpField.Width <- Dim.Fill(Dim.Absolute 2)

    let okBtn = new Button()
    okBtn.Text <- "OK"
    okBtn.IsDefault <- true
    okBtn.Accepting.Add(fun _ ->
        let url = urlField.Text.Trim()
        if url <> "" then
            result <- Some {
                Url      = url
                Name     = optStr nameField.Text
                Branch   = optStr branchField.Text
                BasePath = optStr bpField.Text
            }
            dlg.RequestStop())

    let cancelBtn = new Button()
    cancelBtn.Text <- "Cancel"
    cancelBtn.Accepting.Add(fun _ -> dlg.RequestStop())

    dlg.Add(urlLabel, urlField, nameLabel, nameField, branchLabel, branchField, bpLabel, bpField)
    dlg.AddButton(okBtn)
    dlg.AddButton(cancelBtn)

    Application.Run(dlg, Unchecked.defaultof<_>)

    result
