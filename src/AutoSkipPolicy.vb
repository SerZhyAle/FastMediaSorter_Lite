Option Strict On

''' <summary>
''' The stop rule for the auto-skip chain, as pure arithmetic.
'''
''' It lives outside Main_Form for the same reason ZoomMath does: it is the one part of the
''' skip machinery that can be reasoned about - and tested - without a form, a folder or a
''' decoder. The rest of the mechanism (RequestAutoSkipJump in Main_Form.MediaLoading.vb) is
''' WinForms glue.
'''
''' Why the rule exists at all: skipping an unreadable file re-enters the display path, so a
''' folder where nothing opens would walk the index round for ever. Bounding the chain by the
''' number of files makes "nothing here is readable" a finite answer. The bound used to double
''' as the recursion depth, which is what turned a dropped network share into a
''' StackOverflowException - see SPECIFICATION_LONG_RUN_STABILITY.md A-2.
''' </summary>
Public Module AutoSkipPolicy

    ''' <summary>
    ''' May a chain that has already skipped <paramref name="chain_Length"/> files skip
    ''' another one?
    ''' </summary>
    ''' <param name="chain_Length">Files skipped in this uninterrupted chain, including the
    ''' one about to be attempted (so the first attempt asks with 1).</param>
    ''' <param name="files_In_Folder">Size of the list being walked.</param>
    Public Function ShouldContinue(chain_Length As Integer, files_In_Folder As Integer) As Boolean
        ' Math.Max(1, ..) so a single-file folder - and an empty one, where the caller has
        ' already given up by other means - still gets its one attempt rather than none.
        Return chain_Length <= Math.Max(1, files_In_Folder)
    End Function

    ''' <summary>
    ''' The <c>files_In_Folder</c> a chain is measured against, taken ONCE, when it starts
    ''' (audit 048, A-09).
    '''
    ''' Every skip removes its file from the list, so the live count shrinks as the chain
    ''' grows. Compared with that live count, the k-th continuation was allowed only while
    ''' k &lt;= N - k: a run of more than half the folder unreadable stopped with "no readable
    ''' files" while the readable rest was still ahead. The chain starts right after the
    ''' first unreadable file was dropped, so the folder it walks had one file more than the
    ''' list now holds. Removal already guarantees the chain ends; the bound only has to stop
    ''' a chain that does NOT remove anything.
    ''' </summary>
    ''' <param name="files_Remaining">The list size at the moment the chain starts.</param>
    Public Function BoundAtChainStart(files_Remaining As Integer) As Integer
        Return Math.Max(0, files_Remaining) + 1
    End Function

End Module
